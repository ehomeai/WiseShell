#include "RemoteDirectoryDialog.h"
#include <QDialogButtonBox>
#include <QLabel>
#include <QLineEdit>
#include <QListWidget>
#include <QPushButton>
#include <QToolBar>
#include <QVBoxLayout>
namespace wise {
RemoteDirectoryDialog::RemoteDirectoryDialog(Profile profile, QString secret, QWidget *parent)
    : QDialog(parent), session_(new SftpSession(std::move(profile), std::move(secret), this)) {
    setWindowTitle(QStringLiteral("选择远程目录"));
    resize(560, 440);
    auto *layout = new QVBoxLayout(this);
    path_ = new QLineEdit;
    layout->addWidget(path_);
    auto *bar = new QToolBar;
    bar->addAction(QStringLiteral("上级目录"), this, [this] {
        if (!current_.isEmpty())
            navigate(remoteJoin(current_, ".."));
    });
    bar->addAction(QStringLiteral("转到"), this, [this] { navigate(path_->text()); });
    layout->addWidget(bar);
    list_ = new QListWidget;
    layout->addWidget(list_);
    status_ = new QLabel(QStringLiteral("连接中"));
    layout->addWidget(status_);
    auto *buttons = new QDialogButtonBox(QDialogButtonBox::Ok | QDialogButtonBox::Cancel);
    buttons->button(QDialogButtonBox::Ok)->setText(QStringLiteral("选择当前目录"));
    buttons->button(QDialogButtonBox::Cancel)->setText(QStringLiteral("取消"));
    buttons->button(QDialogButtonBox::Ok)->setEnabled(false);
    layout->addWidget(buttons);
    connect(buttons, &QDialogButtonBox::accepted, this, &QDialog::accept);
    connect(buttons, &QDialogButtonBox::rejected, this, &QDialog::reject);
    connect(path_, &QLineEdit::returnPressed, this, [this] { navigate(path_->text()); });
    connect(list_, &QListWidget::itemDoubleClicked, this,
            [this](QListWidgetItem *item) { navigate(item->data(Qt::UserRole).toString()); });
    connect(session_, &SftpSession::listed, this,
            [this, buttons](QString path, const QList<RemoteEntry> &entries) {
                current_ = path;
                path_->setText(path);
                list_->clear();
                for (const auto &entry : entries)
                    if (entry.directory && !entry.symlink) {
                        auto *item = new QListWidgetItem(entry.name, list_);
                        item->setData(Qt::UserRole, entry.path);
                    }
                status_->setText(QStringLiteral("双击进入目录，确定选择当前目录"));
                buttons->button(QDialogButtonBox::Ok)->setEnabled(true);
            });
    connect(session_, &SftpSession::failed, this,
            [this](QString message, bool) { status_->setText(message); });
    connect(session_, &SftpSession::taskFinished, this, [this](QString, QString message, bool ok) {
        if (!ok)
            status_->setText(message);
    });
}
RemoteDirectoryDialog::~RemoteDirectoryDialog() {
    session_->close();
    session_->wait();
}
void RemoteDirectoryDialog::navigate(const QString &path) {
    FileTask task;
    task.source = path;
    session_->enqueue(task);
}
QString RemoteDirectoryDialog::directory() const {
    return current_;
}
} // namespace wise
