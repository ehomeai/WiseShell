#include "SessionDialog.h"
#include <QCheckBox>
#include <QComboBox>
#include <QDialogButtonBox>
#include <QDoubleSpinBox>
#include <QFileDialog>
#include <QFontComboBox>
#include <QFormLayout>
#include <QHBoxLayout>
#include <QLineEdit>
#include <QMessageBox>
#include <QPushButton>
#include <QSpinBox>
#include <QTabWidget>
#include <QVBoxLayout>
namespace wise {
TerminalSettingsEditor::TerminalSettingsEditor(const TerminalSettings &s, QWidget *parent) : QWidget(parent) {
    auto *layout = new QFormLayout(this);
    theme_ = new QComboBox;
    theme_->addItems({"Dark", "Light", "Solarized"});
    theme_->setCurrentText(s.theme);
    font_ = new QFontComboBox;
    font_->setEditable(true);
    font_->setCurrentFont(QFont(s.fontFamily));
    size_ = new QDoubleSpinBox;
    size_->setRange(9, 72);
    size_->setValue(s.fontSize);
    scrollback_ = new QSpinBox;
    scrollback_->setRange(0, 100000);
    scrollback_->setSingleStep(1000);
    scrollback_->setValue(s.scrollback);
    layout->addRow(QStringLiteral("主题"), theme_);
    layout->addRow(QStringLiteral("字体"), font_);
    layout->addRow(QStringLiteral("字号"), size_);
    layout->addRow(QStringLiteral("历史行数"), scrollback_);
}
TerminalSettings TerminalSettingsEditor::value() const {
    return {theme_->currentText(), font_->currentText(), size_->value(), scrollback_->value()};
}
SessionDialog::SessionDialog(const Profile &p, const QStringList &groups, QWidget *parent)
    : QDialog(parent), original_(p) {
    setWindowTitle(QStringLiteral("会话设置"));
    resize(570, 610);
    auto *layout = new QVBoxLayout(this);
    auto *tabs = new QTabWidget;
    layout->addWidget(tabs);
    auto *connection = new QWidget;
    auto *form = new QFormLayout(connection);
    name_ = new QLineEdit(p.name);
    group_ = new QComboBox;
    group_->setEditable(true);
    group_->addItem("");
    group_->addItems(groups);
    group_->setCurrentText(p.group);
    host_ = new QLineEdit(p.host);
    port_ = new QSpinBox;
    port_->setRange(1, 65535);
    port_->setValue(p.port);
    user_ = new QLineEdit(p.username);
    auth_ = new QComboBox;
    auth_->addItems({QStringLiteral("密码"), QStringLiteral("私钥")});
    auth_->setCurrentIndex(p.keyAuth ? 1 : 0);
    key_ = new QLineEdit(p.privateKey);
    auto *keyRow = new QWidget;
    auto *keyLayout = new QHBoxLayout(keyRow);
    keyLayout->setContentsMargins(0, 0, 0, 0);
    keyLayout->addWidget(key_);
    auto *browse = new QPushButton(QStringLiteral("选择"));
    keyLayout->addWidget(browse);
    connect(browse, &QPushButton::clicked, this, [this] {
        auto path = QFileDialog::getOpenFileName(this, QStringLiteral("选择私钥"));
        if (!path.isEmpty())
            key_->setText(path);
    });
    secret_ = new QLineEdit;
    secret_->setEchoMode(QLineEdit::Password);
    secret_->setPlaceholderText(QStringLiteral("留空保留现有凭据；连接时可输入"));
    remember_ = new QCheckBox(QStringLiteral("记住密码 / 私钥口令"));
    remember_->setChecked(p.remember);
    favorite_ = new QCheckBox(QStringLiteral("收藏此会话"));
    favorite_->setChecked(p.favorite);
    keepAlive_ = new QSpinBox;
    keepAlive_->setRange(0, 3600);
    keepAlive_->setValue(p.keepAlive);
    keepAlive_->setSuffix(QStringLiteral(" 秒（0 关闭）"));
    local_ = new QLineEdit(p.localDirectory);
    auto *localRow = new QWidget;
    auto *ll = new QHBoxLayout(localRow);
    ll->setContentsMargins(0, 0, 0, 0);
    ll->addWidget(local_);
    auto *lb = new QPushButton(QStringLiteral("选择"));
    ll->addWidget(lb);
    connect(lb, &QPushButton::clicked, this, [this] {
        auto path = QFileDialog::getExistingDirectory(this, QStringLiteral("本地默认目录"), local_->text());
        if (!path.isEmpty())
            local_->setText(path);
    });
    remote_ = new QLineEdit(p.remoteDirectory);
    remote_->setPlaceholderText(QStringLiteral("可在 SFTP 浏览器中将当前目录设为默认"));
    form->addRow(QStringLiteral("名称"), name_);
    form->addRow(QStringLiteral("分组"), group_);
    form->addRow(QStringLiteral("主机"), host_);
    form->addRow(QStringLiteral("端口"), port_);
    form->addRow(QStringLiteral("用户名"), user_);
    form->addRow(QStringLiteral("认证"), auth_);
    form->addRow(QStringLiteral("私钥"), keyRow);
    form->addRow(QStringLiteral("密码 / 口令"), secret_);
    form->addRow(remember_);
    form->addRow(favorite_);
    form->addRow(QStringLiteral("保活"), keepAlive_);
    form->addRow(QStringLiteral("本地默认目录"), localRow);
    auto *remoteRow = new QWidget;
    auto *remoteLayout = new QHBoxLayout(remoteRow);
    remoteLayout->setContentsMargins(0, 0, 0, 0);
    remoteLayout->addWidget(remote_);
    auto *remoteBrowse = new QPushButton(QStringLiteral("浏览"));
    remoteLayout->addWidget(remoteBrowse);
    connect(remoteBrowse, &QPushButton::clicked, this, [this] {
        if (host_->text().trimmed().isEmpty() || user_->text().trimmed().isEmpty()) {
            QMessageBox::warning(this, QStringLiteral("连接信息"), QStringLiteral("请先填写主机和用户名。"));
            return;
        }
        emit browseRemote(value());
    });
    form->addRow(QStringLiteral("远程默认目录"), remoteRow);
    tabs->addTab(connection, QStringLiteral("连接"));
    terminal_ = new TerminalSettingsEditor(p.terminal);
    tabs->addTab(terminal_, QStringLiteral("终端"));
    auto *buttons = new QDialogButtonBox(QDialogButtonBox::Save | QDialogButtonBox::Cancel);
    buttons->button(QDialogButtonBox::Save)->setText(QStringLiteral("保存"));
    buttons->button(QDialogButtonBox::Cancel)->setText(QStringLiteral("取消"));
    layout->addWidget(buttons);
    connect(buttons, &QDialogButtonBox::rejected, this, &QDialog::reject);
    connect(buttons, &QDialogButtonBox::accepted, this, [this] {
        if (name_->text().trimmed().isEmpty() || host_->text().trimmed().isEmpty() ||
            user_->text().trimmed().isEmpty() || (auth_->currentIndex() == 1 && key_->text().isEmpty())) {
            QMessageBox::warning(this, QStringLiteral("信息不完整"),
                                 QStringLiteral("请填写名称、主机、用户名，以及私钥认证所需的私钥路径。"));
            return;
        }
        accept();
    });
}
Profile SessionDialog::value() const {
    auto p = original_;
    p.name = name_->text().trimmed();
    p.group = normalizeGroup(group_->currentText());
    p.host = host_->text().trimmed();
    p.port = port_->value();
    p.username = user_->text().trimmed();
    p.keyAuth = auth_->currentIndex() == 1;
    p.privateKey = key_->text();
    p.remember = remember_->isChecked();
    p.favorite = favorite_->isChecked();
    p.keepAlive = keepAlive_->value();
    p.localDirectory = local_->text();
    p.remoteDirectory = remote_->text();
    p.terminal = terminal_->value();
    return p;
}
QString SessionDialog::secret() const {
    return secret_->text();
}
bool SessionDialog::secretChanged() const {
    return secret_->isModified();
}
void SessionDialog::setRemoteDirectory(const QString &path) {
    remote_->setText(path);
}
} // namespace wise
