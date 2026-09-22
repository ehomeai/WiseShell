#include "MainWindow.h"
#include "RemoteDirectoryDialog.h"
#include "SessionDialog.h"
#include "SftpWindow.h"
#include "terminal/TerminalWidget.h"
#include <QCheckBox>
#include <QDialogButtonBox>
#include <QDir>
#include <QDropEvent>
#include <QGridLayout>
#include <QHeaderView>
#include <QInputDialog>
#include <QLabel>
#include <QLineEdit>
#include <QMenu>
#include <QMenuBar>
#include <QMessageBox>
#include <QPalette>
#include <QPainter>
#include <QPointer>
#include <QPushButton>
#include <QSignalBlocker>
#include <QShortcut>
#include <QTabBar>
#include <QSplitter>
#include <QStatusBar>
#include <QStyledItemDelegate>
#include <QStyle>
#include <QTabWidget>
#include <QTableWidget>
#include <QTimer>
#include <QToolBar>
#include <QVBoxLayout>
#include <algorithm>
namespace wise {
namespace {
QString commonShortcuts() {
    return QStringLiteral("常用快捷键\n"
                          "Ctrl+Insert：复制终端选区\n"
                          "Shift+Insert：粘贴到终端\n"
                          "Ctrl+C：发送中断\n"
                          "Shift+PageUp / PageDown：浏览终端历史\n"
                          "F2：重命名当前终端标签页");
}
struct ConnectionFailure {
    QString reason;
    QString advice;
};
ConnectionFailure describeConnectionFailure(const QString &message, bool authentication) {
    const auto lower = message.toLower();
    if (authentication)
        return {QStringLiteral("认证失败"),
                QStringLiteral("检查用户名、密码、私钥路径或私钥口令后重试。")};
    if (lower.contains(QStringLiteral("timeout")) || message.contains(QStringLiteral("超时")))
        return {QStringLiteral("TCP 连接超时"),
                QStringLiteral("确认 VPN / 内网已接入，并检查 SSH 服务、防火墙和端口。")};
    if (lower.contains(QStringLiteral("could not resolve")) || lower.contains(QStringLiteral("name or service")))
        return {QStringLiteral("无法解析主机名"), QStringLiteral("检查会话中的主机地址或 DNS 设置。")};
    if (lower.contains(QStringLiteral("refused")))
        return {QStringLiteral("目标端口拒绝连接"), QStringLiteral("确认 SSH 服务正在运行，且端口配置正确。")};
    if (lower.contains(QStringLiteral("host key rejected")))
        return {QStringLiteral("主机指纹未被信任"), QStringLiteral("核对服务器指纹后重新连接。")};
    const auto detail = message.section('\n', 0, 0).trimmed();
    return {detail.isEmpty() ? QStringLiteral("SSH 连接未建立") : detail,
            QStringLiteral("检查网络、会话配置和服务器日志后重试。")};
}
QString terminalFailureMessage(const Profile &profile, const ConnectionFailure &failure) {
    const auto target = (profile.username.isEmpty() ? QString() : profile.username + "@") + profile.host +
                        ":" + QString::number(profile.port);
    return QStringLiteral("\033[2J\033[H\033[1;31m连接失败\033[0m\r\n\r\n"
                          "\033[90m目标\033[0m  %1\r\n"
                          "\033[90m原因\033[0m  %2\r\n"
                          "\033[90m建议\033[0m  %3\r\n\r\n"
                          "\033[90m按 Enter 立即重连，或关闭此标签页。\033[0m\r\n"
                          "\033[90m详细诊断已在错误对话框中提供。\033[0m\r\n")
        .arg(target, failure.reason, failure.advice);
}
class SessionItemDelegate final : public QStyledItemDelegate {
  public:
    using QStyledItemDelegate::QStyledItemDelegate;

    void paint(QPainter *painter, const QStyleOptionViewItem &option,
               const QModelIndex &index) const override {
        QStyleOptionViewItem item(option);
        initStyleOption(&item, index);
        const bool selected = item.state.testFlag(QStyle::State_Selected);
        item.state &= ~QStyle::State_Selected;
        // QTreeView's style text rect includes the indentation area on some
        // Windows styles.  option.rect already starts at this row's icon;
        // derive the label start from it so the pill never paints before the icon.
        if (index.column() == 0) {
            // Reserve a small, consistent gap after the program icon.  Applying
            // it to every session keeps labels from jumping when selection moves.
            const int iconWidth = item.decorationSize.width() > 0 ? item.decorationSize.width() : 16;
            item.decorationSize.setWidth(iconWidth + 6);
        }
        if (selected && index.column() == 0) {
            const auto text = index.data(Qt::DisplayRole).toString();
            const int iconWidth = item.decorationSize.width() > 0 ? item.decorationSize.width() : 16;
            QRect textRect(item.rect.left() + iconWidth + 4, item.rect.top(), item.rect.width(),
                           item.rect.height());
            const int textWidth = QFontMetrics(item.font).horizontalAdvance(text) + 14;
            textRect.setWidth(std::min(item.rect.right() - textRect.left() + 1, textWidth));
            textRect.adjust(0, 2, 4, -2);
            painter->save();
            painter->setPen(Qt::NoPen);
            painter->setBrush(QColor("#2186eb"));
            painter->drawRect(textRect);
            painter->restore();
            item.palette.setColor(QPalette::Text, Qt::white);
            item.palette.setColor(QPalette::WindowText, Qt::white);
        }
        QStyledItemDelegate::paint(painter, item, index);
    }
};
} // namespace
SessionTree::SessionTree(QWidget *parent) : QTreeWidget(parent) {
    // The view paints a selection behind its branch indicator before the item
    // delegate runs.  Keep that structural area white; the delegate paints the
    // intended blue selection only behind the node label.
    auto colors = palette();
    colors.setColor(QPalette::Active, QPalette::Highlight, QColor("#ffffff"));
    colors.setColor(QPalette::Inactive, QPalette::Highlight, QColor("#ffffff"));
    setPalette(colors);
    setItemDelegate(new SessionItemDelegate(this));
    setObjectName(QStringLiteral("sessionTree"));
    setIndentation(19);
    setIconSize(QSize(16, 16));
    setUniformRowHeights(true);
}
void SessionTree::drawBranches(QPainter *painter, const QRect &rect,
                               const QModelIndex &index) const {
    painter->save();
    painter->setPen(QPen(QColor("#a0a0a0"), 1, Qt::DotLine));
    const int x = rect.right() - indentation() / 2;
    const int y = rect.center().y();
    painter->drawLine(x, rect.top(), x, y);
    if (index.row() + 1 < model()->rowCount(index.parent()))
        painter->drawLine(x, y, x, rect.bottom());
    painter->drawLine(x, y, rect.right(), y);
    auto ancestor = index.parent();
    for (int ax = x - indentation(); ancestor.isValid(); ax -= indentation()) {
        if (ancestor.row() + 1 < model()->rowCount(ancestor.parent()))
            painter->drawLine(ax, rect.top(), ax, rect.bottom());
        ancestor = ancestor.parent();
    }
    if (model()->hasChildren(index)) {
        painter->setPen(QPen(QColor("#808890"), 1));
        painter->setBrush(Qt::white);
        painter->drawRect(QRect(x - 4, y - 4, 8, 8));
        painter->drawLine(x - 2, y, x + 2, y);
        if (!isExpanded(index))
            painter->drawLine(x, y - 2, x, y + 2);
    }
    painter->restore();
}
void SessionTree::dropEvent(QDropEvent *e) {
    auto *source = currentItem();
    auto *target = itemAt(e->position().toPoint());
    if (!source || !source->parent() || source == target) {
        e->ignore();
        return;
    }
    QString group;
    if (target) {
        if (!target->data(0, Qt::UserRole).toString().isEmpty())
            target = target->parent();
        if (target)
            group = target->data(0, Qt::UserRole + 1).toString();
    }
    auto id = source->data(0, Qt::UserRole).toString(), old = source->data(0, Qt::UserRole + 1).toString();
    // Do not let QTreeWidget delete/move its internal items after a model rebuild.
    e->ignore();
    QTimer::singleShot(0, this, [this, id, old, group] { emit moved(id, old, group); });
}
MainWindow::MainWindow(QWidget *parent, QString directory)
    : QMainWindow(parent), repository_(directory), hosts_(directory), credentials_(this) {
    setWindowTitle(QStringLiteral("WiseShell C++"));
    setWindowIcon(QIcon(":/logo.png"));
    resize(1280, 820);
    try {
        workspace_ = repository_.load();
    } catch (const std::exception &e) {
        throw std::runtime_error(e.what());
    }
    auto *split = new QSplitter;
    setCentralWidget(split);
    auto *side = new QWidget;
    auto *sl = new QVBoxLayout(side);
    sl->setContentsMargins(8, 8, 8, 8);
    search_ = new QLineEdit;
    search_->setPlaceholderText(QStringLiteral("搜索会话、主机或分组"));
    sl->addWidget(search_);
    favorites_ = new QCheckBox(QStringLiteral("仅显示收藏"));
    sl->addWidget(favorites_);
    tree_ = new SessionTree;
    tree_->setHeaderLabel(QStringLiteral("会话"));
    // A double click opens a terminal; renaming is deliberately an explicit menu action.
    tree_->setEditTriggers(QAbstractItemView::NoEditTriggers);
    tree_->setDragDropMode(QAbstractItemView::InternalMove);
    tree_->setDefaultDropAction(Qt::MoveAction);
    tree_->setContextMenuPolicy(Qt::CustomContextMenu);
    sl->addWidget(tree_, 1);
    properties_ = new QTableWidget(6, 2);
    properties_->setObjectName(QStringLiteral("sessionProperties"));
    properties_->setHorizontalHeaderLabels({QStringLiteral("属性"), QStringLiteral("值")});
    properties_->horizontalHeader()->hide();
    properties_->verticalHeader()->hide();
    properties_->horizontalHeader()->setSectionResizeMode(QHeaderView::Stretch);
    properties_->verticalHeader()->setDefaultSectionSize(25);
    properties_->setSelectionBehavior(QAbstractItemView::SelectItems);
    properties_->setSelectionMode(QAbstractItemView::SingleSelection);
    properties_->setEditTriggers(QAbstractItemView::NoEditTriggers);
    properties_->setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    properties_->setVerticalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    properties_->setFixedHeight(152);
    sl->addWidget(properties_);
    updateProperties(nullptr);
    split->addWidget(side);
    tabs_ = new QTabWidget;
    tabs_->setTabsClosable(true);
    tabs_->setMovable(true);
    auto *renameTab = new QShortcut(QKeySequence(Qt::Key_F2), tabs_);
    renameTab->setContext(Qt::WidgetWithChildrenShortcut);
    connect(renameTab, &QShortcut::activated, this, [this] {
        QPointer<QWidget> page = tabs_->currentWidget();
        if (!page || !terminals_.contains(page))
            return;
        auto *bar = tabs_->tabBar();
        if (bar->findChild<QLineEdit *>(QStringLiteral("tabNameEditor")))
            return;
        auto *editor = new QLineEdit(tabs_->tabText(tabs_->currentIndex()), bar);
        editor->setObjectName(QStringLiteral("tabNameEditor"));
        editor->setGeometry(bar->tabRect(tabs_->currentIndex()));
        auto *cancel = new QShortcut(QKeySequence(Qt::Key_Escape), editor);
        cancel->setContext(Qt::WidgetWithChildrenShortcut);
        connect(cancel, &QShortcut::activated, editor, [editor] {
            editor->setProperty("cancelled", true);
            editor->hide();
            editor->deleteLater();
        });
        connect(editor, &QLineEdit::editingFinished, editor, [this, editor, page] {
            const auto name = editor->text().trimmed();
            const int index = page ? tabs_->indexOf(page) : -1;
            if (!editor->property("cancelled").toBool() && index >= 0 && !name.isEmpty())
                tabs_->setTabText(index, name);
            editor->deleteLater();
        });
        editor->show();
        editor->setFocus();
        editor->selectAll();
    });
    split->addWidget(tabs_);
    split->setSizes({280, 1000});
    auto *welcome = new QWidget;
    auto *welcomeLayout = new QVBoxLayout(welcome);
    welcomeLayout->setContentsMargins(24, 24, 24, 24);
    auto *welcomeContent = new QWidget(welcome);
    auto *contentLayout = new QVBoxLayout(welcomeContent);
    contentLayout->setContentsMargins(0, 0, 0, 0);
    contentLayout->setSpacing(12);
    auto *title = new QLabel(QStringLiteral("WiseShell"));
    auto titleFont = title->font();
    titleFont.setPointSizeF(titleFont.pointSizeF() + 4);
    titleFont.setBold(true);
    title->setFont(titleFont);
    title->setAlignment(Qt::AlignCenter);
    contentLayout->addWidget(title);
    contentLayout->addSpacing(4);
    for (const auto &text : {QStringLiteral("新建会话，开始 SSH 连接"),
                             QStringLiteral("双击会话打开终端 · 工具栏打开 SFTP")}) {
        auto *label = new QLabel(text);
        label->setAlignment(Qt::AlignCenter);
        contentLayout->addWidget(label);
    }
    contentLayout->addSpacing(12);
    const auto shortcutLines = commonShortcuts().split('\n');
    auto *shortcutTitle = new QLabel(shortcutLines.first());
    auto headingFont = shortcutTitle->font();
    headingFont.setBold(true);
    shortcutTitle->setFont(headingFont);
    contentLayout->addWidget(shortcutTitle);
    auto *shortcutLayout = new QGridLayout;
    shortcutLayout->setContentsMargins(0, 0, 0, 0);
    shortcutLayout->setHorizontalSpacing(24);
    shortcutLayout->setVerticalSpacing(10);
    for (int row = 1; row < shortcutLines.size(); ++row) {
        const auto &line = shortcutLines.at(row);
        const auto separator = line.indexOf(QChar(u'：'));
        shortcutLayout->addWidget(new QLabel(line.left(separator)), row - 1, 0,
                                  Qt::AlignLeft | Qt::AlignVCenter);
        shortcutLayout->addWidget(new QLabel(line.mid(separator + 1)), row - 1, 1,
                                  Qt::AlignLeft | Qt::AlignVCenter);
    }
    contentLayout->addLayout(shortcutLayout);
    welcomeLayout->addWidget(welcomeContent, 0, Qt::AlignCenter);
    tabs_->addTab(welcome, QStringLiteral("欢迎"));
    auto *bar = addToolBar(QStringLiteral("主工具栏"));
    bar->setMovable(false);
    bar->addAction(QStringLiteral("新建会话"), this, [this] { editSession(true); });
    bar->addAction(QStringLiteral("新建分组"), this, &MainWindow::addGroup);
    bar->addAction(QStringLiteral("编辑"), this, [this] { editSession(false); });
    bar->addSeparator();
    bar->addAction(QStringLiteral("连接终端"), this, [this] { connectSelected(false); });
    bar->addAction(QStringLiteral("SFTP"), this, &MainWindow::currentSftp);
    bar->addAction(QStringLiteral("重连"), this, &MainWindow::reconnectCurrent);
    bar->addAction(QStringLiteral("断开当前"), this, [this] {
        if (auto *s = terminals_.value(tabs_->currentWidget()))
            s->close();
    });
    status_ = new QLabel(QStringLiteral("就绪 · 数据目录：") + dataDirectory());
    statusBar()->addWidget(status_, 1);
    auto *fileMenu = menuBar()->addMenu(QStringLiteral("文件(&F)"));
    fileMenu->addAction(
        QStringLiteral("新建会话"), this, [this] { editSession(true); }, QKeySequence::New);
    fileMenu->addAction(QStringLiteral("新建分组"), this, &MainWindow::addGroup);
    fileMenu->addSeparator();
    fileMenu->addAction(QStringLiteral("退出"), this, &QWidget::close, QKeySequence::Quit);
    auto *editMenu = menuBar()->addMenu(QStringLiteral("编辑(&E)"));
    editMenu->addAction(QStringLiteral("编辑会话"), this, [this] { editSession(false); });
    editMenu->addAction(QStringLiteral("重命名"), this, [this] {
        if (tree_->currentItem() && tree_->currentItem()->parent())
            tree_->editItem(tree_->currentItem(), 0);
    });
    editMenu->addAction(QStringLiteral("删除会话 / 分组"), this, &MainWindow::removeSelected);
    auto *toolsMenu = menuBar()->addMenu(QStringLiteral("工具(&T)"));
    toolsMenu->addAction(QStringLiteral("SFTP 文件浏览器"), this, &MainWindow::currentSftp);
    toolsMenu->addAction(QStringLiteral("重连当前会话"), this, &MainWindow::reconnectCurrent);
    toolsMenu->addAction(QStringLiteral("终端默认设置"), this, &MainWindow::defaults);
    auto *helpMenu = menuBar()->addMenu(QStringLiteral("帮助(&H)"));
    helpMenu->addAction(QStringLiteral("关于 WiseShell"), this, [this] {
        QMessageBox::about(this, QStringLiteral("WiseShell C++"),
                           QStringLiteral("WiseShell C++ 1.0\n原生 SSH / SFTP 客户端\n\n") +
                               commonShortcuts());
    });
    connect(tree_, &QTreeWidget::currentItemChanged, this,
            [this] { updateProperties(selectedProfile()); });
    connect(search_, &QLineEdit::textChanged, this, [this] { rebuild(); });
    connect(favorites_, &QCheckBox::toggled, this, [this] { rebuild(); });
    connect(tree_, &QTreeWidget::itemDoubleClicked, this, [this](QTreeWidgetItem *i, int) {
        if (!i->data(0, Qt::UserRole).toString().isEmpty())
            connectSelected(false);
    });
    connect(tabs_, &QTabWidget::tabCloseRequested, this, &MainWindow::closeTab);
    connect(tabs_, &QTabWidget::currentChanged, this, [this](int) {
        if (auto *widget = tabs_->currentWidget()) {
            status_->setText(widget->property("status").toString());
            widget->setFocus();
        }
    });
    connect(tree_, &QTreeWidget::customContextMenuRequested, this, [this](QPoint pos) {
        if (auto *i = tree_->itemAt(pos))
            tree_->setCurrentItem(i);
        QMenu menu(this);
        menu.addAction(QStringLiteral("新建会话"), this, [this] { editSession(true); });
        menu.addAction(QStringLiteral("新建分组"), this, &MainWindow::addGroup);
        if (auto *p = selectedProfile()) {
            menu.addAction(QStringLiteral("连接"), this, [this] { connectSelected(false); });
            menu.addAction(QStringLiteral("SFTP"), this, [this] { connectSelected(true); });
            menu.addAction(QStringLiteral("编辑"), this, [this] { editSession(false); });
            menu.addAction(QStringLiteral("复制会话"), this, [this] {
                if (auto *s = selectedProfile()) {
                    auto p = *s;
                    p.id = QUuid::createUuid().toString(QUuid::WithoutBraces);
                    p.name += QStringLiteral(" 副本");
                    p.remember = false;
                    workspace_.sessions.append(p);
                    save();
                    rebuild();
                }
            });
            menu.addAction(p->favorite ? QStringLiteral("取消收藏") : QStringLiteral("收藏"), this, [this] {
                if (auto *p = selectedProfile()) {
                    p->favorite = !p->favorite;
                    save();
                    rebuild();
                }
            });
        }
        if (tree_->currentItem() && tree_->currentItem()->parent()) {
            menu.addAction(QStringLiteral("重命名"), this,
                           [this] { tree_->editItem(tree_->currentItem(), 0); });
            menu.addAction(QStringLiteral("删除"), this, &MainWindow::removeSelected);
        }
        menu.exec(tree_->viewport()->mapToGlobal(pos));
    });
    connect(tree_, &QTreeWidget::itemChanged, this, [this](QTreeWidgetItem *item, int) {
        if (rebuilding_ || !item->parent())
            return;
        const auto id = item->data(0, Qt::UserRole).toString();
        const auto name = item->text(0).trimmed();
        if (name.isEmpty() || name.contains('/') || name.contains('\\')) {
            rebuild();
            return;
        }
        if (!id.isEmpty()) {
            for (auto &p : workspace_.sessions)
                if (p.id == id)
                    p.name = name;
        } else {
            const auto old = item->data(0, Qt::UserRole + 1).toString();
            const auto parent =
                item->parent() ? item->parent()->data(0, Qt::UserRole + 1).toString() : QString();
            try {
                moveGroup(workspace_, old, normalizeGroup(parent + "/" + name));
            } catch (const std::exception &e) {
                showError(QString::fromUtf8(e.what()));
            }
        }
        save();
        QTimer::singleShot(0, this, [this] { rebuild(); });
    });
    connect(tree_, &SessionTree::moved, this, [this](QString id, QString old, QString target) {
        try {
            if (id.isEmpty())
                moveGroup(workspace_, old, normalizeGroup(target + "/" + old.section('/', -1)));
            else
                for (auto &p : workspace_.sessions)
                    if (p.id == id)
                        p.group = target;
            save();
            rebuild();
        } catch (const std::exception &e) {
            showError(QString::fromUtf8(e.what()));
        }
    });
    rebuild();
}
MainWindow::~MainWindow() {
    for (auto *s : terminals_)
        s->close();
    for (auto *s : terminals_)
        s->wait();
    for (auto *window : findChildren<SftpWindow *>())
        delete window;
}
void MainWindow::showError(const QString &message) {
    QMessageBox::warning(this, QStringLiteral("WiseShell"), message);
}
bool MainWindow::save() {
    try {
        repository_.save(workspace_);
        return true;
    } catch (const std::exception &e) {
        showError(QString::fromUtf8(e.what()));
        return false;
    }
}
Profile *MainWindow::selectedProfile() {
    auto *i = tree_->currentItem();
    if (!i)
        return nullptr;
    const auto id = i->data(0, Qt::UserRole).toString();
    for (auto &p : workspace_.sessions)
        if (p.id == id)
            return &p;
    return nullptr;
}
void MainWindow::updateProperties(const Profile *profile) {
    static const QStringList labels = {QStringLiteral("名称"), QStringLiteral("主机"),
                                       QStringLiteral("端口"), QStringLiteral("协议"),
                                       QStringLiteral("用户名"), QStringLiteral("说明")};
    QStringList values(6);
    if (profile)
        values = {profile->name, profile->host, QString::number(profile->port), QStringLiteral("SSH"),
                  profile->username, QString()};
    for (int row = 0; row < labels.size(); ++row) {
        auto set = [this, row](int column, const QString &text) {
            auto *item = properties_->item(row, column);
            if (!item) {
                item = new QTableWidgetItem;
                item->setFlags(Qt::ItemIsEnabled | Qt::ItemIsSelectable);
                properties_->setItem(row, column, item);
            }
            item->setText(text);
        };
        set(0, labels[row]);
        set(1, values[row]);
    }
}
QString MainWindow::selectedGroup() const {
    auto *i = tree_->currentItem();
    return i ? i->data(0, Qt::UserRole + 1).toString() : QString();
}
void MainWindow::rebuild() {
    QString selectedId, selectedPath;
    if (auto *i = tree_->currentItem()) {
        selectedId = i->data(0, Qt::UserRole).toString();
        selectedPath = i->data(0, Qt::UserRole + 1).toString();
    }
    rebuilding_ = true;
    QSignalBlocker blocker(tree_);
    tree_->clear();
    QHash<QString, QTreeWidgetItem *> groups;
    auto *root = new QTreeWidgetItem(tree_);
    root->setText(0, QStringLiteral("所有会话"));
    root->setIcon(0, style()->standardIcon(QStyle::SP_DirIcon));
    root->setFlags(Qt::ItemIsEnabled | Qt::ItemIsSelectable | Qt::ItemIsDropEnabled);
    groups[QString()] = root;
    auto ensure = [&](const QString &path) {
        QTreeWidgetItem *parent = root;
        QString current;
        for (const auto &part : normalizeGroup(path).split('/', Qt::SkipEmptyParts)) {
            current = current.isEmpty() ? part : current + "/" + part;
            if (!groups.contains(current)) {
                auto *item = parent ? new QTreeWidgetItem(parent) : new QTreeWidgetItem(tree_);
                item->setText(0, part);
                item->setData(0, Qt::UserRole + 1, current);
                item->setFlags(item->flags() | Qt::ItemIsEditable | Qt::ItemIsDragEnabled |
                               Qt::ItemIsDropEnabled);
                item->setIcon(0, style()->standardIcon(QStyle::SP_DirIcon));
                groups[current] = item;
            }
            parent = groups[current];
        }
        return parent;
    };
    for (const auto &g : workspace_.folders)
        ensure(g);
    const auto filter = search_->text().trimmed();
    for (const auto &p : workspace_.sessions) {
        if (favorites_->isChecked() && !p.favorite)
            continue;
        if (!filter.isEmpty() &&
            !(p.name + " " + p.host + " " + p.group).contains(filter, Qt::CaseInsensitive))
            continue;
        auto *parent = ensure(p.group);
        auto *item = parent ? new QTreeWidgetItem(parent) : new QTreeWidgetItem(tree_);
        item->setText(0, p.name);
        item->setToolTip(0, p.username + "@" + p.host + ":" + QString::number(p.port));
        item->setData(0, Qt::UserRole, p.id);
        item->setData(0, Qt::UserRole + 1, p.group);
        item->setFlags((item->flags() | Qt::ItemIsEditable | Qt::ItemIsDragEnabled) & ~Qt::ItemIsDropEnabled);
        item->setIcon(0, windowIcon());
        if (p.id == selectedId)
            tree_->setCurrentItem(item);
    }
    if (selectedId.isEmpty() && groups.contains(selectedPath))
        tree_->setCurrentItem(groups[selectedPath]);
    tree_->sortItems(0, Qt::AscendingOrder);
    tree_->expandAll();
    rebuilding_ = false;
}
void MainWindow::editSession(bool create) {
    Profile p;
    if (create) {
        p.group = selectedGroup();
        p.terminal = workspace_.defaults;
    } else {
        auto *selected = selectedProfile();
        if (!selected)
            return;
        p = *selected;
    }
    SessionDialog dialog(p, workspace_.folders, this);
    connect(&dialog, &SessionDialog::browseRemote, this,
            [this, guard = QPointer<SessionDialog>(&dialog)](Profile profile) {
                auto browse = [this, guard, profile](QString secret) {
                    if (!guard)
                        return;
                    RemoteDirectoryDialog picker(profile, secret, guard);
                    connect(picker.session(), &SftpSession::verifyHost, &picker,
                            [this](const HostKey &key, DecisionPtr decision) { verify(key, decision); });
                    connect(picker.session(), &SftpSession::failed, &picker,
                            [this, profile](QString, bool auth) {
                                if (auth)
                                    credentials_.invalidate(profile.id);
                            });
                    picker.session()->start();
                    if (picker.exec() == QDialog::Accepted && guard)
                        guard->setRemoteDirectory(picker.directory());
                };
                if (guard && guard->secretChanged())
                    browse(guard->secret());
                else {
                    profile.remember = false;
                    withSecret(profile, browse);
                }
            });
    if (dialog.exec() != QDialog::Accepted)
        return;
    const auto updated = dialog.value();
    if (create)
        workspace_.sessions.append(updated);
    else
        for (auto &s : workspace_.sessions)
            if (s.id == p.id)
                s = updated;
    if (!updated.group.isEmpty() && !workspace_.folders.contains(updated.group))
        workspace_.folders.append(updated.group);
    if (save()) {
        if (updated.host != p.host || updated.username != p.username || updated.keyAuth != p.keyAuth ||
            updated.privateKey != p.privateKey)
            credentials_.invalidate(updated.id);
        if (!updated.remember) {
            credentials_.remove(updated.id, [this, updated, secret = dialog.secret(),
                                             changed = dialog.secretChanged()](QString, QString error) {
                if (!error.isEmpty())
                    showError(QStringLiteral("删除已保存凭据失败：\n") + error);
                if (changed)
                    credentials_.cache(updated.id, secret);
            });
        } else if (dialog.secretChanged())
            // Persist only after authentication succeeds, never while editing.
            credentials_.cache(updated.id, dialog.secret());
    }
    rebuild();
}
void MainWindow::addGroup() {
    bool ok = false;
    auto name = QInputDialog::getText(this, QStringLiteral("新建分组"), QStringLiteral("分组名称"),
                                      QLineEdit::Normal, {}, &ok)
                    .trimmed();
    if (!ok || name.isEmpty())
        return;
    const auto path = normalizeGroup(selectedGroup() + "/" + name);
    if (!workspace_.folders.contains(path))
        workspace_.folders.append(path);
    save();
    rebuild();
}
void MainWindow::removeSelected() {
    auto *item = tree_->currentItem();
    if (!item || !item->parent())
        return;
    if (QMessageBox::question(this, QStringLiteral("删除"),
                              QStringLiteral("删除选中的会话或分组及其所有会话？"),
                              QMessageBox::Yes | QMessageBox::No, QMessageBox::No) != QMessageBox::Yes)
        return;
    const auto id = item->data(0, Qt::UserRole).toString(),
               group = item->data(0, Qt::UserRole + 1).toString();
    for (auto it = workspace_.sessions.begin(); it != workspace_.sessions.end();) {
        if ((!id.isEmpty() && it->id == id) ||
            (id.isEmpty() && (it->group == group || it->group.startsWith(group + "/")))) {
            credentials_.remove(it->id, [this](QString, QString error) {
                if (!error.isEmpty())
                    showError(error);
            });
            it = workspace_.sessions.erase(it);
        } else
            ++it;
    }
    if (id.isEmpty())
        workspace_.folders.removeIf(
            [&](const QString &s) { return s == group || s.startsWith(group + "/"); });
    save();
    rebuild();
}
void MainWindow::withSecret(Profile p, std::function<void(QString)> done) {
    auto consume = [this, p, done](QString secret, QString error) {
        if (!error.isEmpty())
            status_->setText(QStringLiteral("系统凭据存储不可用；请手动输入"));
        if (secret.isEmpty()) {
            bool ok = false;
            secret = QInputDialog::getText(
                this, p.name, p.keyAuth ? QStringLiteral("私钥口令（无口令可留空）") : QStringLiteral("密码"),
                QLineEdit::Password, {}, &ok);
            if (!ok)
                return;
            credentials_.cache(p.id, secret);
        }
        done(secret);
    };
    if (p.remember)
        credentials_.read(p.id, consume);
    else
        consume(credentials_.runtime(p.id), {});
}
void MainWindow::rememberSuccessfulSecret(const Profile &profile, QString secret) {
    if (!profile.remember || secret.isEmpty())
        return;
    credentials_.write(profile.id, secret, [this, profile](QString, QString error) {
        if (!error.isEmpty()) {
            status_->setText(QStringLiteral("已连接；系统凭据存储不可用，密码仅保留到退出程序"));
            return;
        }
        for (auto &saved : workspace_.sessions)
            if (saved.id == profile.id)
                saved.remember = true;
        save();
        status_->setText(QStringLiteral("已连接；密码已保存到系统安全存储"));
    });
    secret.fill(QChar(0));
}
void MainWindow::connectSelected(bool sftp) {
    if (auto *selected = selectedProfile()) {
        const auto p = *selected;
        withSecret(p, [this, p, sftp](QString secret) {
            if (sftp)
                openSftp(p, secret);
            else
                openTerminal(p, secret);
        });
    }
}
void MainWindow::currentSftp() {
    if (auto *widget = tabs_->currentWidget()) {
        const auto id = widget->property("profileId").toString();
        for (const auto &p : workspace_.sessions)
            if (p.id == id) {
                withSecret(p, [this, p](QString secret) { openSftp(p, secret); });
                return;
            }
    }
    connectSelected(true);
}
void MainWindow::reconnectCurrent() {
    if (auto *widget = tabs_->currentWidget()) {
        const auto id = widget->property("profileId").toString();
        for (const auto &p : workspace_.sessions)
            if (p.id == id) {
                const auto profile = p;
                closeTab(tabs_->currentIndex());
                withSecret(profile, [this, profile](QString secret) { openTerminal(profile, secret); });
                return;
            }
    }
    connectSelected(false);
}
void MainWindow::verify(const HostKey &key, DecisionPtr decision) {
    try {
        auto trust = hosts_.check(key);
        if (trust == Trust::Match) {
            decision->resolve(1);
            return;
        }
        const auto message =
            (trust == Trust::Changed
                 ? QStringLiteral(
                       "警告：服务器主机指纹发生变化。仅在确认服务器密钥已更换后替换信任记录。\n\n")
                 : QStringLiteral("首次连接此服务器，是否信任此主机指纹？\n\n")) +
            key.host + ":" + QString::number(key.port) + "\n" + key.algorithm + "\n" + key.fingerprint;
        if (QMessageBox::question(this, QStringLiteral("验证主机指纹"), message,
                                  QMessageBox::Yes | QMessageBox::No, QMessageBox::No) == QMessageBox::Yes) {
            hosts_.trust(key);
            decision->resolve(1);
        } else
            decision->resolve(0);
    } catch (const std::exception &e) {
        decision->resolve(0);
        showError(QString::fromUtf8(e.what()));
    }
}
void MainWindow::openTerminal(Profile p, QString secret) {
    auto *terminal = new TerminalWidget;
    terminal->setSettings(p.terminal);
    auto savedSecret = std::make_shared<QString>(secret);
    auto saved = std::make_shared<bool>(false);
    auto *session = new TerminalSession(p, std::move(secret), this);
    terminals_[terminal] = session;
    terminal->setProperty("profileId", p.id);
    tabs_->setCurrentIndex(tabs_->addTab(terminal, p.name));
    connect(terminal, &TerminalWidget::input, session, &TerminalSession::sendInput);
    connect(terminal, &TerminalWidget::terminalResized, session, &TerminalSession::resizeTerminal);
    connect(session, &TerminalSession::output, terminal, &TerminalWidget::feed);
    connect(session, &TerminalSession::verifyHost, terminal,
            [this](const HostKey &key, DecisionPtr decision) { verify(key, decision); });
    connect(session, &TerminalSession::state, terminal, [this, terminal, p, savedSecret, saved](QString state) {
        auto size = terminal->terminalSize();
        QString text = p.name + " · " + p.username + "@" + p.host + ":" + QString::number(p.port) +
                       QString(" · %1×%2 · ").arg(size.width()).arg(size.height()) + state;
        terminal->setProperty("connectionState", state);
        terminal->setProperty("status", text);
        if (tabs_->currentWidget() == terminal)
            status_->setText(text);
        if (state.startsWith(QStringLiteral("已连接")) && !*saved) {
            *saved = true;
            rememberSuccessfulSecret(p, *savedSecret);
            savedSecret->fill(QChar(0));
            savedSecret->clear();
        } else if (state == QStringLiteral("已断开")) {
            savedSecret->fill(QChar(0));
            savedSecret->clear();
        }
    });
    connect(terminal, &TerminalWidget::terminalResized, this, [this, terminal, p](int c, int r) {
        QString text = p.name + " · " + p.host + QString(" · %1×%2 · ").arg(c).arg(r) +
                       terminal->property("connectionState").toString();
        terminal->setProperty("status", text);
        if (tabs_->currentWidget() == terminal)
            status_->setText(text);
    });
    connect(session, &TerminalSession::failed, terminal,
            [this, p, terminal, session, savedSecret](QString message, bool auth) {
        if (auth)
            credentials_.invalidate(p.id);
        savedSecret->fill(QChar(0));
        savedSecret->clear();
        const auto failure = describeConnectionFailure(message, auth);
        terminal->feed(terminalFailureMessage(p, failure).toUtf8());
        const auto reconnect = [this, p, terminal] {
            if (terminal->property("reconnecting").toBool())
                return;

            terminal->setProperty("reconnecting", true);
            terminal->feed("\r\n\033[90m正在重新连接…\033[0m\r\n");
            const auto tabIndex = tabs_->indexOf(terminal);
            if (tabIndex >= 0)
                closeTab(tabIndex);
            withSecret(p, [this, p](QString secret) { openTerminal(p, secret); });
        };
        if (tabs_->currentWidget() == terminal)
            terminal->setFocus();

        // The failed session cannot accept useful input.  Enter on the failure page
        // starts a new connection using the normal credential flow.
        disconnect(terminal, &TerminalWidget::input, session, &TerminalSession::sendInput);
        connect(terminal, &TerminalWidget::input, terminal,
                [reconnect](const QByteArray &bytes) {
                    if (!bytes.contains('\r') && !bytes.contains('\n')) {
                        return;
                    }
                    reconnect();
                });
    });
    auto size = terminal->terminalSize();
    session->resizeTerminal(size.width(), size.height());
    session->start();
    terminal->setFocus();
}
void MainWindow::openSftp(Profile p, QString secret) {
    auto savedSecret = std::make_shared<QString>(secret);
    auto saved = std::make_shared<bool>(false);
    auto *window = new SftpWindow(p, std::move(secret), this);
    window->setWindowFlag(Qt::Window);
    connect(window->session(), &SftpSession::verifyHost, window,
            [this](const HostKey &key, DecisionPtr decision) { verify(key, decision); });
    connect(window->session(), &SftpSession::state, window,
            [this, p, savedSecret, saved](QString state) {
                if (state == QStringLiteral("已连接") && !*saved) {
                    *saved = true;
                    rememberSuccessfulSecret(p, *savedSecret);
                    savedSecret->fill(QChar(0));
                    savedSecret->clear();
                } else if (state == QStringLiteral("已断开")) {
                    savedSecret->fill(QChar(0));
                    savedSecret->clear();
                }
            });
    connect(window->session(), &SftpSession::failed, window,
            [this, p, savedSecret](QString, bool auth) {
        if (auth)
            credentials_.invalidate(p.id);
        savedSecret->fill(QChar(0));
        savedSecret->clear();
    });
    connect(window, &SftpWindow::defaultDirectories, this, [this](QString id, QString local, QString remote) {
        for (auto &p : workspace_.sessions)
            if (p.id == id) {
                p.localDirectory = local;
                p.remoteDirectory = remote;
            }
        save();
        status_->setText(QStringLiteral("已保存默认目录"));
    });
    window->show();
    window->session()->start();
}
void MainWindow::closeTab(int index) {
    auto *widget = tabs_->widget(index);
    if (!widget)
        return;
    if (auto *session = terminals_.take(widget)) {
        session->close();
        connect(session, &QThread::finished, session, &QObject::deleteLater);
        if (!session->isRunning())
            session->deleteLater();
    }
    tabs_->removeTab(index);
    widget->deleteLater();
}
void MainWindow::defaults() {
    QDialog dialog(this);
    dialog.setWindowTitle(QStringLiteral("终端默认设置"));
    auto *layout = new QVBoxLayout(&dialog);
    auto *editor = new TerminalSettingsEditor(workspace_.defaults);
    layout->addWidget(editor);
    auto *apply = new QCheckBox(QStringLiteral("更新仍使用旧默认值的会话"));
    apply->setChecked(true);
    layout->addWidget(apply);
    auto *buttons = new QDialogButtonBox(QDialogButtonBox::Save | QDialogButtonBox::Cancel);
    buttons->button(QDialogButtonBox::Save)->setText(QStringLiteral("保存"));
    buttons->button(QDialogButtonBox::Cancel)->setText(QStringLiteral("取消"));
    layout->addWidget(buttons);
    connect(buttons, &QDialogButtonBox::accepted, &dialog, &QDialog::accept);
    connect(buttons, &QDialogButtonBox::rejected, &dialog, &QDialog::reject);
    if (dialog.exec() == QDialog::Accepted) {
        applyDefaults(workspace_, editor->value(), apply->isChecked());
        save();
        for (auto *widget : terminals_.keys())
            for (const auto &p : workspace_.sessions)
                if (p.id == widget->property("profileId").toString())
                    static_cast<TerminalWidget *>(widget)->setSettings(p.terminal);
    }
}
} // namespace wise
