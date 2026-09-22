#include "SftpWindow.h"
#include <QCloseEvent>
#include <QContextMenuEvent>
#include <QDateTime>
#include <QDir>
#include <QFile>
#include <QFileIconProvider>
#include <QHeaderView>
#include <QHBoxLayout>
#include <QInputDialog>
#include <QJsonDocument>
#include <QJsonObject>
#include <QLabel>
#include <QLineEdit>
#include <QMenu>
#include <QMessageBox>
#include <QProgressBar>
#include <QPushButton>
#include <QSplitter>
#include <QStatusBar>
#include <QStyle>
#include <QTableWidget>
#include <QTimer>
#include <QToolBar>
#include <QVBoxLayout>
#include <array>
namespace wise {
static bool safeName(const QString &n) {
    return !n.isEmpty() && n != "." && n != ".." && !n.contains('/') && !n.contains('\\') &&
           !n.contains(QChar(0));
}
namespace {
QString displaySize(quint64 bytes) {
    static constexpr std::array<const char *, 5> units{"B", "KB", "MB", "GB", "TB"};
    double value = bytes;
    int unit = 0;
    while (value >= 1024. && unit < int(units.size()) - 1) {
        value /= 1024.;
        ++unit;
    }
    return unit == 0 ? QString::number(bytes) + " B"
                     : QString::number(value, 'f', value < 10. ? 1 : 0) + " " + units[unit];
}
QString displayTime(const QDateTime &time) {
    return time.isValid() ? time.toLocalTime().toString(QStringLiteral("yyyy-MM-dd HH:mm:ss")) : QString();
}
QString directorySummary(int files, int directories, quint64 bytes) {
    return QStringLiteral("%1 个文件和 %2 个目录，大小总计：%3")
        .arg(files)
        .arg(directories)
        .arg(displaySize(bytes));
}
QString transferDirection(FileOperation operation) {
    switch (operation) {
    case FileOperation::Upload:
        return QStringLiteral("上传 →");
    case FileOperation::Download:
        return QStringLiteral("← 下载");
    case FileOperation::Mkdir:
        return QStringLiteral("新建目录");
    case FileOperation::Rename:
        return QStringLiteral("重命名");
    case FileOperation::Remove:
        return QStringLiteral("删除");
    case FileOperation::List:
        return QStringLiteral("刷新");
    }
    return {};
}
void configureBrowser(QTableWidget *view, const QStringList &columns) {
    view->setColumnCount(columns.size());
    view->setHorizontalHeaderLabels(columns);
    view->setSelectionBehavior(QAbstractItemView::SelectRows);
    view->setSelectionMode(QAbstractItemView::ExtendedSelection);
    view->setEditTriggers(QAbstractItemView::NoEditTriggers);
    view->setShowGrid(false);
    view->setAlternatingRowColors(false);
    view->verticalHeader()->hide();
    view->verticalHeader()->setDefaultSectionSize(24);
    view->horizontalHeader()->setStretchLastSection(false);
    view->horizontalHeader()->setSectionResizeMode(0, QHeaderView::Stretch);
    for (int column = 1; column < columns.size(); ++column)
        view->horizontalHeader()->setSectionResizeMode(column, QHeaderView::ResizeToContents);
}
void appendParentRow(QTableWidget *view, const QString &parent, const QIcon &icon) {
    const int row = view->rowCount();
    view->insertRow(row);
    auto *name = new QTableWidgetItem(icon, QStringLiteral(".."));
    name->setData(Qt::UserRole, parent);
    name->setData(Qt::UserRole + 1, true);
    view->setItem(row, 0, name);
    for (int column = 1; column < view->columnCount(); ++column)
        view->setItem(row, column, new QTableWidgetItem);
}
} // namespace
SftpWindow::SftpWindow(Profile p, QString secret, QWidget *parent)
    : QMainWindow(parent), profile_(std::move(p)),
      session_(new SftpSession(profile_, std::move(secret), this)) {
    setAttribute(Qt::WA_DeleteOnClose);
    setWindowTitle(profile_.name + QStringLiteral(" — SFTP"));
    resize(1180, 760);
    auto *central = new QWidget;
    auto *layout = new QVBoxLayout(central);
    layout->setContentsMargins(0, 0, 0, 0);
    layout->setSpacing(0);
    setCentralWidget(central);
    auto *split = new QSplitter(Qt::Horizontal);
    split->setChildrenCollapsible(false);
    split->setHandleWidth(4);
    auto makePanel = [&](QString title, QLineEdit *&path, QTableWidget *&view, QLabel *&summary,
                         const QStringList &columns) {
        auto *panel = new QWidget;
        auto *l = new QVBoxLayout(panel);
        l->setContentsMargins(5, 4, 5, 4);
        l->setSpacing(3);
        auto *location = new QHBoxLayout;
        location->setContentsMargins(0, 0, 0, 0);
        auto *label = new QLabel(title);
        label->setObjectName(QStringLiteral("sftpLocationLabel"));
        location->addWidget(label);
        path = new QLineEdit;
        path->setClearButtonEnabled(true);
        path->setObjectName(title == QStringLiteral("本地站点:") ? QStringLiteral("localPathInput")
                                                                  : QStringLiteral("remotePathInput"));
        location->addWidget(path, 1);
        l->addLayout(location);
        view = new QTableWidget;
        view->setObjectName(title == QStringLiteral("本地站点:") ? QStringLiteral("sftpLocalView")
                                                                : QStringLiteral("sftpRemoteView"));
        configureBrowser(view, columns);
        l->addWidget(view, 1);
        summary = new QLabel;
        summary->setObjectName(QStringLiteral("sftpSummary"));
        l->addWidget(summary);
        split->addWidget(panel);
    };
    makePanel(QStringLiteral("本地站点:"), localPath_, localView_, localSummary_,
              {QStringLiteral("文件名"), QStringLiteral("文件大小"), QStringLiteral("文件类型"),
               QStringLiteral("最后修改")});
    makePanel(QStringLiteral("远程站点:"), remotePath_, remoteView_, remoteSummary_,
              {QStringLiteral("文件名"), QStringLiteral("文件大小"), QStringLiteral("最后修改")});
    layout->addWidget(split, 3);
    split->setSizes({width() / 2, width() / 2});
    transfers_ = new QTableWidget(0, 7);
    transfers_->setObjectName(QStringLiteral("transferQueue"));
    transfers_->setHorizontalHeaderLabels({QStringLiteral("本地文件"), QStringLiteral("方向"),
                                           QStringLiteral("远程文件"), QStringLiteral("大小"),
                                           QStringLiteral("进度"), QStringLiteral("状态"),
                                           QStringLiteral("速度")});
    transfers_->horizontalHeader()->setSectionResizeMode(0, QHeaderView::Stretch);
    transfers_->horizontalHeader()->setSectionResizeMode(1, QHeaderView::ResizeToContents);
    transfers_->horizontalHeader()->setSectionResizeMode(2, QHeaderView::Stretch);
    for (int column = 3; column < transfers_->columnCount(); ++column)
        transfers_->horizontalHeader()->setSectionResizeMode(column, QHeaderView::ResizeToContents);
    transfers_->setSelectionBehavior(QAbstractItemView::SelectRows);
    transfers_->setEditTriggers(QAbstractItemView::NoEditTriggers);
    transfers_->setShowGrid(false);
    transfers_->verticalHeader()->hide();
    transfers_->verticalHeader()->setDefaultSectionSize(31);
    transfers_->setMinimumHeight(126);
    transfers_->setMaximumHeight(210);
    transfers_->setVisible(false);
    layout->addWidget(transfers_);
    auto *bar = new QToolBar(QStringLiteral("文件操作"), this);
    addToolBar(Qt::BottomToolBarArea, bar);
    bar->setObjectName(QStringLiteral("sftpToolbar"));
    bar->setMovable(false);
    bar->addAction(QStringLiteral("本地上级"), this,
                   [this] { localNavigate(QDir(localPath_->text()).absoluteFilePath("..")); });
    bar->addAction(QStringLiteral("远程上级"), this,
                   [this] { remoteNavigate(remoteJoin(remoteDirectory_, "..")); });
    bar->addAction(QStringLiteral("刷新"), this, &SftpWindow::refresh);
    bar->addSeparator();
    bar->addAction(QStringLiteral("上传 →"), this, [this] { transfer(true); });
    bar->addAction(QStringLiteral("← 下载"), this, [this] { transfer(false); });
    bar->addAction(QStringLiteral("取消选中任务"), this, &SftpWindow::cancelSelectedTransfers);
    bar->addAction(QStringLiteral("传输队列"), this,
                   [this] { transfers_->setVisible(!transfers_->isVisible()); });
    bar->addAction(QStringLiteral("设为默认目录"), this, [this] {
        if (!remoteDirectory_.isEmpty())
            emit defaultDirectories(profile_.id, localPath_->text(), remoteDirectory_);
    });
    for (bool remote : {false, true}) {
        bar->addSeparator();
        bar->addAction(remote ? QStringLiteral("远程新建目录") : QStringLiteral("本地新建目录"), this,
                       [this, remote] { fileAction(remote, FileOperation::Mkdir); });
        bar->addAction(remote ? QStringLiteral("远程重命名") : QStringLiteral("本地重命名"), this,
                       [this, remote] { fileAction(remote, FileOperation::Rename); });
        bar->addAction(remote ? QStringLiteral("远程删除") : QStringLiteral("本地删除"), this,
                       [this, remote] { fileAction(remote, FileOperation::Remove); });
    }
    state_ = new QLabel(QStringLiteral("连接中"));
    statusBar()->addWidget(state_);
    connect(localPath_, &QLineEdit::returnPressed, this, [this] { localNavigate(localPath_->text()); });
    connect(remotePath_, &QLineEdit::returnPressed, this, [this] { remoteNavigate(remotePath_->text()); });
    connect(localView_, &QTableWidget::cellDoubleClicked, this, [this](int row, int) {
        auto *item = localView_->item(row, 0);
        if (item && item->data(Qt::UserRole + 1).toBool())
            localNavigate(item->data(Qt::UserRole).toString());
    });
    connect(remoteView_, &QTableWidget::cellDoubleClicked, this, [this](int row, int) {
        auto *item = remoteView_->item(row, 0);
        if (item && item->data(Qt::UserRole + 1).toBool()) {
            remoteNavigate(item->data(Qt::UserRole).toString());
            return;
        }
        auto e = item->data(Qt::UserRole).value<RemoteEntry>();
        if (e.directory && !e.symlink)
            remoteNavigate(e.path);
    });
    for (auto *view : {localView_, remoteView_, transfers_}) {
        view->installEventFilter(this);
        view->viewport()->installEventFilter(this);
    }
    connect(session_, &SftpSession::state, this, [this](QString state) {
        if (!state_->property("connectionFailed").toBool())
            state_->setText(state);
    });
    connect(session_, &SftpSession::failed, this, [this](QString message, bool) {
        state_->setProperty("connectionFailed", true);
        state_->setText(QStringLiteral("连接失败：") + message);
        state_->setToolTip(message);
        state_->setWordWrap(true);
    });
    connect(session_, &SftpSession::listed, this, [this](QString path, const QList<RemoteEntry> &entries) {
        remoteDirectory_ = path;
        remotePath_->setText(path);
        remoteView_->setRowCount(0);
        int files = 0;
        int directories = 0;
        quint64 bytes = 0;
        appendParentRow(remoteView_, remoteJoin(path, QStringLiteral("..")),
                        style()->standardIcon(QStyle::SP_FileDialogToParent));
        for (const auto &e : entries) {
            int r = remoteView_->rowCount();
            remoteView_->insertRow(r);
            auto *name = new QTableWidgetItem(
                e.directory ? style()->standardIcon(QStyle::SP_DirIcon)
                            : style()->standardIcon(QStyle::SP_FileIcon),
                e.name);
            name->setData(Qt::UserRole, QVariant::fromValue(e));
            name->setData(Qt::UserRole + 1, false);
            remoteView_->setItem(r, 0, name);
            remoteView_->setItem(r, 1, new QTableWidgetItem(e.directory ? QString() : displaySize(e.size)));
            remoteView_->setItem(r, 2, new QTableWidgetItem(displayTime(e.modified)));
            if (e.directory)
                ++directories;
            else {
                ++files;
                bytes += e.size;
            }
        }
        remoteSummary_->setText(directorySummary(files, directories, bytes));
    });
    connect(session_, &SftpSession::conflict, this, [this](const QString &path, DecisionPtr d) {
        if (closing_) {
            d->resolve(0);
            return;
        }
        QMessageBox box(QMessageBox::Question, QStringLiteral("文件已存在"), path, QMessageBox::NoButton,
                        this);
        auto *overwrite = box.addButton(QStringLiteral("覆盖"), QMessageBox::AcceptRole);
        auto *skip = box.addButton(QStringLiteral("跳过"), QMessageBox::DestructiveRole);
        box.addButton(QMessageBox::Cancel);
        box.exec();
        d->resolve(box.clickedButton() == overwrite ? 1 : box.clickedButton() == skip ? 2 : 0);
    });
    connect(session_, &SftpSession::taskStarted, this, [this](QString id) {
        int r = taskRow(id);
        if (r >= 0) {
            transfers_->setVisible(true);
            transfers_->item(r, 5)->setText(QStringLiteral("正在传输"));
            if (auto *bar = qobject_cast<QProgressBar *>(transfers_->cellWidget(r, 4)))
                bar->setFormat(QStringLiteral("准备中"));
        }
    });
    connect(session_, &SftpSession::progress, this,
            [this](QString id, quint64 done, quint64 total, double speed) {
                int r = taskRow(id);
                if (r < 0)
                    return;
                transfers_->setVisible(true);
                transfers_->item(r, 3)->setText(displaySize(total));
                const int percent = total ? int(std::clamp<quint64>(done * 100 / total, 0, 100)) : 100;
                if (auto *bar = qobject_cast<QProgressBar *>(transfers_->cellWidget(r, 4))) {
                    bar->setValue(percent);
                    bar->setFormat(QStringLiteral("%1 / %2 · %p%")
                                       .arg(displaySize(done), displaySize(total)));
                }
                transfers_->item(r, 6)->setText(QString::number(speed / 1024 / 1024, 'f', 1) + " MiB/s");
            });
    connect(session_, &SftpSession::taskFinished, this, [this](QString id, QString result, bool success) {
        int r = taskRow(id);
        if (r >= 0) {
            transfers_->setVisible(true);
            transfers_->item(r, 5)->setText(result);
            if (auto *bar = qobject_cast<QProgressBar *>(transfers_->cellWidget(r, 4))) {
                if (success)
                    bar->setValue(100);
                bar->setFormat(success ? QStringLiteral("已完成 · 100%") : result);
            }
        }
        if (active_.contains(id)) {
            const auto task = active_.take(id);
            if (task.operation != FileOperation::List) {
                auto dir = QDir(dataDirectory()).filePath("Logs");
                QDir().mkpath(dir);
                QFile log(QDir(dir).filePath("transfers.jsonl"));
                if (log.size() > 2 * 1024 * 1024) {
                    QFile::remove(log.fileName() + ".1");
                    log.rename(log.fileName() + ".1");
                    log.setFileName(QDir(dir).filePath("transfers.jsonl"));
                }
                if (log.open(QIODevice::WriteOnly | QIODevice::Append))
                    log.write(QJsonDocument(
                                  QJsonObject{{"time", QDateTime::currentDateTimeUtc().toString(Qt::ISODate)},
                                              {"source", task.source},
                                              {"destination", task.destination},
                                              {"result", result}})
                                  .toJson(QJsonDocument::Compact) +
                              "\n");
                if (success && !closing_)
                    refresh();
            }
        }
        if (!success && r < 0 && !closing_)
            state_->setText(result);
        trimHistory();
    });
    localNavigate(profile_.localDirectory.isEmpty() ? QDir::homePath() : profile_.localDirectory);
    // The owner attaches host verification and authentication handlers before starting.
}
SftpWindow::~SftpWindow() {
    session_->close();
    session_->wait();
}
void SftpWindow::closeEvent(QCloseEvent *e) {
    closing_ = true;
    session_->close();
    QMainWindow::closeEvent(e);
}
bool SftpWindow::eventFilter(QObject *watched, QEvent *event) {
    if (event->type() != QEvent::ContextMenu)
        return QMainWindow::eventFilter(watched, event);

    const auto *contextEvent = static_cast<QContextMenuEvent *>(event);
    const auto handleBrowser = [this, watched, contextEvent](QTableWidget *view, bool remote) {
        if (watched != view && watched != view->viewport())
            return false;
        const auto viewPosition = watched == view ? contextEvent->pos()
                                                   : view->viewport()->mapTo(view, contextEvent->pos());
        const auto viewportPosition = view->viewport()->mapFrom(view, viewPosition);
        const auto index = view->indexAt(viewportPosition);
        if (index.isValid()) {
            if (!view->selectionModel()->isRowSelected(index.row(), QModelIndex())) {
                view->clearSelection();
                view->selectRow(index.row());
            }
            view->setCurrentCell(index.row(), 0);
        } else {
            view->clearSelection();
        }
        showBrowserMenu(remote, contextEvent->globalPos());
        return true;
    };
    if (handleBrowser(localView_, false) || handleBrowser(remoteView_, true))
        return true;

    if (watched != transfers_ && watched != transfers_->viewport())
        return QMainWindow::eventFilter(watched, event);
    const auto viewPosition = watched == transfers_ ? contextEvent->pos()
                                                      : transfers_->viewport()->mapTo(transfers_, contextEvent->pos());
    const auto viewportPosition = transfers_->viewport()->mapFrom(transfers_, viewPosition);
    const auto index = transfers_->indexAt(viewportPosition);
    if (index.isValid()) {
        if (!transfers_->selectionModel()->isRowSelected(index.row(), QModelIndex())) {
            transfers_->clearSelection();
            transfers_->selectRow(index.row());
        }
        transfers_->setCurrentCell(index.row(), 0);
    } else {
        transfers_->clearSelection();
    }
    auto *menu = new QMenu(this);
    connect(menu, &QMenu::aboutToHide, menu, &QObject::deleteLater);
    auto *cancel = menu->addAction(QStringLiteral("取消选中任务"));
    cancel->setEnabled(!transfers_->selectionModel()->selectedRows().isEmpty());
    connect(cancel, &QAction::triggered, this, &SftpWindow::cancelSelectedTransfers);
    menu->popup(contextEvent->globalPos());
    return true;
}
void SftpWindow::localNavigate(QString path) {
    path = path.trimmed();
    if (path.isEmpty()) {
        localPath_->setText(localPath_->text());
        return;
    }
    if (QDir::isRelativePath(path))
        path = QDir(localPath_->text()).absoluteFilePath(path);
    QDir dir(path);
    if (!dir.exists()) {
        QMessageBox::warning(this, QStringLiteral("目录不可用"), path);
        return;
    }
    localPath_->setText(QDir::toNativeSeparators(dir.absolutePath()));
    populateLocal(dir.absolutePath());
}
void SftpWindow::remoteNavigate(QString path) {
    path = path.trimmed();
    if (path.isEmpty()) {
        remotePath_->setText(remoteDirectory_);
        return;
    }
    if (!path.startsWith('/') && !remoteDirectory_.isEmpty())
        path = remoteJoin(remoteDirectory_, path);
    FileTask t;
    t.source = path;
    session_->enqueue(t);
}
void SftpWindow::populateLocal(const QString &path) {
    QDir dir(path);
    localView_->setRowCount(0);
    QFileIconProvider icons;
    appendParentRow(localView_, dir.absoluteFilePath(QStringLiteral("..")),
                    style()->standardIcon(QStyle::SP_FileDialogToParent));
    const auto entries = dir.entryInfoList(QDir::AllEntries | QDir::NoDotAndDotDot | QDir::Hidden | QDir::System,
                                           QDir::DirsFirst | QDir::Name | QDir::IgnoreCase);
    int files = 0;
    int directories = 0;
    quint64 bytes = 0;
    for (const auto &info : entries) {
        const int row = localView_->rowCount();
        localView_->insertRow(row);
        auto *name = new QTableWidgetItem(icons.icon(info), info.fileName());
        name->setData(Qt::UserRole, info.absoluteFilePath());
        name->setData(Qt::UserRole + 1, info.isDir() && !info.isSymLink());
        localView_->setItem(row, 0, name);
        localView_->setItem(row, 1, new QTableWidgetItem(info.isDir() ? QString() : displaySize(info.size())));
        localView_->setItem(row, 2,
                            new QTableWidgetItem(info.isSymLink() ? QStringLiteral("符号链接")
                                                                  : info.isDir() ? QStringLiteral("文件夹")
                                                                                 : QStringLiteral("文件")));
        localView_->setItem(row, 3, new QTableWidgetItem(displayTime(info.lastModified())));
        if (info.isDir())
            ++directories;
        else {
            ++files;
            bytes += quint64(info.size());
        }
    }
    localSummary_->setText(directorySummary(files, directories, bytes));
}
void SftpWindow::refresh() {
    localNavigate(localPath_->text());
    if (!remoteDirectory_.isEmpty())
        remoteNavigate(remoteDirectory_);
}
int SftpWindow::taskRow(const QString &id) const {
    for (int r = 0; r < transfers_->rowCount(); ++r)
        if (transfers_->item(r, 0)->data(Qt::UserRole).toString() == id)
            return r;
    return -1;
}
void SftpWindow::trimHistory() {
    for (int r = 0; transfers_->rowCount() > 200 && r < transfers_->rowCount();) {
        if (!active_.contains(transfers_->item(r, 0)->data(Qt::UserRole).toString()))
            transfers_->removeRow(r);
        else
            ++r;
    }
}
void SftpWindow::queue(FileTask t) {
    if (!session_->isRunning()) {
        QMessageBox::warning(this, QStringLiteral("SFTP"), QStringLiteral("连接已关闭，请重新打开 SFTP。"));
        return;
    }
    active_[t.id] = t;
    transfers_->setVisible(true);
    int row = transfers_->rowCount();
    transfers_->insertRow(row);
    const bool upload = t.operation == FileOperation::Upload;
    const bool download = t.operation == FileOperation::Download;
    const QString local = upload ? t.source : download ? t.destination : t.source;
    const QString remote = upload ? t.destination : download ? t.source : t.destination;
    auto *localItem = new QTableWidgetItem(local);
    localItem->setData(Qt::UserRole, t.id);
    transfers_->setItem(row, 0, localItem);
    transfers_->setItem(row, 1, new QTableWidgetItem(transferDirection(t.operation)));
    transfers_->setItem(row, 2, new QTableWidgetItem(remote));
    transfers_->setItem(row, 3, new QTableWidgetItem(QStringLiteral("计算中")));
    auto *bar = new QProgressBar;
    bar->setObjectName(QStringLiteral("transferProgress"));
    bar->setRange(0, 100);
    bar->setValue(0);
    bar->setFormat(QStringLiteral("排队中"));
    transfers_->setCellWidget(row, 4, bar);
    transfers_->setItem(row, 5, new QTableWidgetItem(QStringLiteral("排队中")));
    transfers_->setItem(row, 6, new QTableWidgetItem);
    session_->enqueue(t);
    trimHistory();
}
void SftpWindow::transfer(bool upload) {
    if (remoteDirectory_.isEmpty())
        return;
    if (upload) {
        for (const auto &i : localView_->selectionModel()->selectedRows()) {
            auto *item = localView_->item(i.row(), 0);
            if (!item || item->data(Qt::UserRole + 1).toBool())
                continue;
            QFileInfo info(item->data(Qt::UserRole).toString());
            FileTask t;
            t.operation = FileOperation::Upload;
            t.source = info.absoluteFilePath();
            t.destination = remoteJoin(remoteDirectory_, info.fileName());
            queue(t);
        }
    } else
        for (const auto &i : remoteView_->selectionModel()->selectedRows()) {
            auto *item = remoteView_->item(i.row(), 0);
            if (!item || item->data(Qt::UserRole + 1).toBool())
                continue;
            auto e = item->data(Qt::UserRole).value<RemoteEntry>();
            FileTask t;
            t.operation = FileOperation::Download;
            t.source = e.path;
            t.destination = QDir(localPath_->text()).filePath(e.name);
            queue(t);
        }
}
void SftpWindow::showBrowserMenu(bool remote, const QPoint &globalPosition) {
    auto *view = remote ? remoteView_ : localView_;
    const auto selectedRows = view->selectionModel()->selectedRows();
    bool hasActionableSelection = false;
    for (const auto &row : selectedRows) {
        const auto *item = view->item(row.row(), 0);
        if (item && item->text() != QStringLiteral("..")) {
            hasActionableSelection = true;
            break;
        }
    }

    QString navigationTarget;
    if (selectedRows.size() == 1) {
        const auto *item = view->item(selectedRows.front().row(), 0);
        if (item) {
            if (!remote && item->data(Qt::UserRole + 1).toBool()) {
                navigationTarget = item->data(Qt::UserRole).toString();
            } else if (remote && item->text() == QStringLiteral("..")) {
                navigationTarget = item->data(Qt::UserRole).toString();
            } else if (remote) {
                const auto entry = item->data(Qt::UserRole).value<RemoteEntry>();
                if (entry.directory && !entry.symlink)
                    navigationTarget = entry.path;
            }
        }
    }

    auto *menu = new QMenu(this);
    connect(menu, &QMenu::aboutToHide, menu, &QObject::deleteLater);
    if (!navigationTarget.isEmpty()) {
        auto *navigate = menu->addAction(selectedRows.front().row() == 0
                                             ? (remote ? QStringLiteral("远程上级")
                                                       : QStringLiteral("本地上级"))
                                             : QStringLiteral("进入目录"));
        connect(navigate, &QAction::triggered, this, [this, remote, navigationTarget] {
            if (remote)
                remoteNavigate(navigationTarget);
            else
                localNavigate(navigationTarget);
        });
        menu->addSeparator();
    }

    auto *transferAction =
        menu->addAction(remote ? QStringLiteral("下载到本地 ←") : QStringLiteral("上传到远程 →"));
    transferAction->setEnabled(hasActionableSelection && !remoteDirectory_.isEmpty() &&
                               !localPath_->text().trimmed().isEmpty());
    connect(transferAction, &QAction::triggered, this, [this, remote] { transfer(!remote); });

    menu->addSeparator();
    auto *mkdir = menu->addAction(remote ? QStringLiteral("远程新建目录")
                                         : QStringLiteral("本地新建目录"));
    connect(mkdir, &QAction::triggered, this,
            [this, remote] { fileAction(remote, FileOperation::Mkdir); });
    auto *rename = menu->addAction(remote ? QStringLiteral("远程重命名")
                                          : QStringLiteral("本地重命名"));
    rename->setEnabled(hasActionableSelection && selectedRows.size() == 1);
    connect(rename, &QAction::triggered, this,
            [this, remote] { fileAction(remote, FileOperation::Rename); });
    auto *remove = menu->addAction(remote ? QStringLiteral("远程删除")
                                          : QStringLiteral("本地删除"));
    remove->setEnabled(hasActionableSelection);
    connect(remove, &QAction::triggered, this,
            [this, remote] { fileAction(remote, FileOperation::Remove); });

    menu->addSeparator();
    auto *refreshAction = menu->addAction(QStringLiteral("刷新"));
    connect(refreshAction, &QAction::triggered, this, &SftpWindow::refresh);
    menu->popup(globalPosition);
}
void SftpWindow::cancelSelectedTransfers() {
    for (const auto &i : transfers_->selectionModel()->selectedRows()) {
        if (auto *item = transfers_->item(i.row(), 0))
            session_->cancel(item->data(Qt::UserRole).toString());
    }
}
void SftpWindow::fileAction(bool remote, FileOperation op) {
    QStringList paths;
    QString parent = remote ? remoteDirectory_ : localPath_->text();
    if (parent.isEmpty())
        return;
    if (op != FileOperation::Mkdir) {
        if (remote) {
            for (const auto &i : remoteView_->selectionModel()->selectedRows()) {
                auto *item = remoteView_->item(i.row(), 0);
                if (item && !item->data(Qt::UserRole + 1).toBool())
                    paths.append(item->data(Qt::UserRole).value<RemoteEntry>().path);
            }
        } else
            for (const auto &i : localView_->selectionModel()->selectedRows()) {
                auto *item = localView_->item(i.row(), 0);
                if (item && !item->data(Qt::UserRole + 1).toBool())
                    paths.append(item->data(Qt::UserRole).toString());
            }
        if (paths.isEmpty())
            return;
    }
    QString name;
    if (op == FileOperation::Mkdir || op == FileOperation::Rename) {
        bool ok = false;
        name = QInputDialog::getText(
            this, op == FileOperation::Mkdir ? QStringLiteral("新建目录") : QStringLiteral("重命名"),
            QStringLiteral("名称"), QLineEdit::Normal,
            op == FileOperation::Rename ? QFileInfo(paths.first()).fileName() : QString(), &ok);
        if (!ok)
            return;
        if (!safeName(name)) {
            QMessageBox::warning(this, QStringLiteral("名称无效"),
                                 QStringLiteral("请输入单个文件或目录名称。"));
            return;
        }
    }
    if (op == FileOperation::Remove &&
        QMessageBox::question(this, QStringLiteral("确认删除"),
                              QStringLiteral("永久删除以下项目？\n") + paths.join('\n'),
                              QMessageBox::Yes | QMessageBox::No, QMessageBox::No) != QMessageBox::Yes)
        return;
    if (remote) {
        if (op == FileOperation::Mkdir) {
            FileTask t;
            t.operation = op;
            t.source = remoteJoin(parent, name);
            queue(t);
        } else
            for (const auto &path : paths) {
                FileTask t;
                t.operation = op;
                t.source = path;
                if (op == FileOperation::Rename)
                    t.destination = remoteJoin(parent, name);
                queue(t);
                if (op == FileOperation::Rename)
                    break;
            }
    } else {
        bool success = true;
        if (op == FileOperation::Mkdir)
            success = QDir(parent).mkdir(name);
        else
            for (const auto &path : paths) {
                QFileInfo info(path);
                if (op == FileOperation::Rename) {
                    success = QDir().rename(path, QDir(info.absolutePath()).filePath(name));
                    break;
                }
                if (info.isDir() && !info.isSymLink())
                    success = QDir(path).removeRecursively();
                else
                    success = QFile::remove(path);
                if (!success)
                    break;
            }
        if (!success)
            QMessageBox::warning(this, QStringLiteral("操作失败"),
                                 QStringLiteral("请检查权限、目录占用或同名项目。"));
    }
}
} // namespace wise
