#include "Connection.h"
#include <QFile>
#include <mutex>
namespace wise {
Connection::~Connection() {
    if (session_) {
        ssh_disconnect(session_);
        ssh_free(session_);
    }
}
QString Connection::error() const {
    return QString::fromUtf8(ssh_get_error(session_));
}
void Connection::connect(const Profile &p, const QString &secret, const VerifyHost &verify,
                         const std::atomic_bool &cancel) {
    static std::once_flag init;
    std::call_once(init, [] { ssh_init(); });
    session_ = ssh_new();
    if (!session_)
        throw std::runtime_error("SSH allocation failed");
    auto host = p.host.toUtf8(), user = p.username.toUtf8();
    unsigned port = unsigned(p.port);
    long timeout = 10;
    ssh_options_set(session_, SSH_OPTIONS_HOST, host.constData());
    ssh_options_set(session_, SSH_OPTIONS_USER, user.constData());
    ssh_options_set(session_, SSH_OPTIONS_PORT, &port);
    ssh_options_set(session_, SSH_OPTIONS_TIMEOUT, &timeout);
    int processConfig = 0;
    ssh_options_set(session_, SSH_OPTIONS_PROCESS_CONFIG, &processConfig);
    if (cancel.load())
        throw std::runtime_error("Cancelled");
    if (ssh_connect(session_) != SSH_OK) {
        const auto detail = error();
        if (detail.contains("timeout", Qt::CaseInsensitive)) {
            throw std::runtime_error(
                QStringLiteral("无法连接 %1:%2：TCP 连接超时。请确认已接入目标内网或 VPN，并检查 SSH 服务、"
                               "防火墙和端口设置。\n\n底层错误：%3")
                    .arg(p.host)
                    .arg(p.port)
                    .arg(detail)
                    .toStdString());
        }
        throw std::runtime_error(detail.toStdString());
    }
    ssh_key key = nullptr;
    if (ssh_get_server_publickey(session_, &key) != SSH_OK)
        throw std::runtime_error(error().toStdString());
    unsigned char *hash = nullptr;
    size_t length = 0;
    const QString algorithm = QString::fromLatin1(ssh_key_type_to_char(ssh_key_type(key)));
    const int rc = ssh_get_publickey_hash(key, SSH_PUBLICKEY_HASH_SHA256, &hash, &length);
    ssh_key_free(key);
    if (rc != SSH_OK)
        throw std::runtime_error("Cannot hash host key");
    const QString fingerprint =
        "SHA256:" +
        QString::fromLatin1(
            QByteArray(reinterpret_cast<char *>(hash), int(length)).toBase64(QByteArray::OmitTrailingEquals));
    ssh_clean_pubkey_hash(&hash);
    if (cancel.load() || !verify({p.host, p.port, algorithm, fingerprint}))
        throw std::runtime_error("Host key rejected / cancelled");
    int auth = SSH_AUTH_DENIED;
    auto bytes = secret.toUtf8();
    if (p.keyAuth) {
        ssh_key privateKey = nullptr;
        QFile keyFile(p.privateKey);
        if (!keyFile.open(QIODevice::ReadOnly) || keyFile.size() > 1024 * 1024) {
            bytes.fill(0);
            throw AuthError("Cannot read private key file");
        }
        auto keyData = keyFile.readAll();
        const int imported = ssh_pki_import_privkey_base64(keyData.constData(), bytes.constData(), nullptr,
                                                           nullptr, &privateKey);
        keyData.fill(0);
        if (imported != SSH_OK) {
            bytes.fill(0);
            throw AuthError("Cannot load private key: invalid path, format or passphrase");
        }
        auth = ssh_userauth_publickey(session_, nullptr, privateKey);
        ssh_key_free(privateKey);
    } else
        auth = ssh_userauth_password(session_, nullptr, bytes.constData());
    bytes.fill(0);
    if (auth != SSH_AUTH_SUCCESS)
        throw AuthError(error().toStdString());
    if (cancel.load())
        throw std::runtime_error("Cancelled");
}
} // namespace wise
