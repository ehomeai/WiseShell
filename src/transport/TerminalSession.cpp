#include "TerminalSession.h"
#include <QDateTime>
#include <QDir>
#include <QElapsedTimer>
#include <QFile>
#include <algorithm>
#include <libssh/callbacks.h>
namespace wise {
TerminalSession::TerminalSession(Profile p, QString secret, QObject *parent)
    : QThread(parent), profile_(std::move(p)), secret_(std::move(secret)) {}
TerminalSession::~TerminalSession() {
    close();
    wait();
}
void TerminalSession::close() {
    stop_ = true;
}
void TerminalSession::sendInput(const QByteArray &b) {
    QMutexLocker lock(&mutex_);
    if (input_.size() + b.size() <= 4 * 1024 * 1024)
        input_.append(b);
}
void TerminalSession::resizeTerminal(int c, int r) {
    QMutexLocker lock(&mutex_);
    size_ = {std::max(2, c), std::max(1, r)};
}
void TerminalSession::run() {
    emit state(QStringLiteral("连接中"));
    try {
        Connection connection;
        connection.connect(
            profile_, secret_,
            [this](const HostKey &h) {
                auto d = std::make_shared<Decision>();
                emit verifyHost(h, d);
                return d->wait(stop_) == 1;
            },
            stop_);
        secret_.fill(QChar(0));
        secret_.clear();
        auto channel = std::unique_ptr<ssh_channel_struct, decltype(&ssh_channel_free)>(
            ssh_channel_new(connection.get()), ssh_channel_free);
        if (!channel || ssh_channel_open_session(channel.get()) != SSH_OK)
            throw std::runtime_error(connection.error().toStdString());
        QSize applied;
        {
            QMutexLocker lock(&mutex_);
            applied = size_;
        }
        if (ssh_channel_request_pty_size(channel.get(), "xterm-256color", applied.width(),
                                         applied.height()) != SSH_OK ||
            ssh_channel_request_shell(channel.get()) != SSH_OK)
            throw std::runtime_error(connection.error().toStdString());
        if (!profile_.remoteDirectory.isEmpty()) {
            auto dir = profile_.remoteDirectory;
            dir.replace("'", "'\\''");
            sendInput(("cd -- '" + dir + "'\r").toUtf8());
        }
        const auto logDir = QDir(dataDirectory()).filePath("Logs");
        QDir().mkpath(logDir);
        QFile log(QDir(logDir).filePath(
            profile_.id + "-" + QDateTime::currentDateTime().toString("yyyyMMdd-HHmmss-zzz") + ".log"));
        const bool logging = log.open(QIODevice::WriteOnly | QIODevice::Append);
        emit state(logging ? QStringLiteral("已连接 · 日志开启") : QStringLiteral("已连接 · 日志不可写"));
        ssh_set_blocking(connection.get(), 0);
        QElapsedTimer keepAlive;
        keepAlive.start();
        QByteArray pending;
        while (!stop_.load()) {
            QSize requested;
            {
                QMutexLocker lock(&mutex_);
                pending.append(input_);
                input_.clear();
                requested = size_;
            }
            if (requested != applied) {
                int rc = ssh_channel_change_pty_size(channel.get(), requested.width(), requested.height());
                if (rc == SSH_OK)
                    applied = requested;
                else if (rc != SSH_AGAIN)
                    throw std::runtime_error(connection.error().toStdString());
            }
            if (!pending.isEmpty()) {
                int n = ssh_channel_write(channel.get(), pending.constData(),
                                          uint32_t(std::min<qsizetype>(pending.size(), 32768)));
                if (n > 0)
                    pending.remove(0, n);
                else if (n != SSH_AGAIN && n < 0)
                    throw std::runtime_error(connection.error().toStdString());
            }
            bool hadOutput = false;
            for (int stream = 0; stream < 2; ++stream) {
                char buffer[32768];
                int n = ssh_channel_read_nonblocking(channel.get(), buffer, sizeof buffer, stream);
                if (n > 0) {
                    hadOutput = true;
                    QByteArray b(buffer, n);
                    emit output(b);
                    if (logging && log.write(b) != b.size())
                        emit state(QStringLiteral("已连接 · 日志写入失败"));
                } else if (n == SSH_ERROR)
                    throw std::runtime_error(connection.error().toStdString());
            }
            if (ssh_channel_is_eof(channel.get()) || !ssh_is_connected(connection.get()))
                break;
            if (profile_.keepAlive > 0 && keepAlive.elapsed() >= profile_.keepAlive * 1000LL) {
                ssh_send_ignore(connection.get(), "");
                keepAlive.restart();
            }
            if (!hadOutput)
                QThread::msleep(8);
        }
        ssh_channel_send_eof(channel.get());
        ssh_channel_close(channel.get());
    } catch (const AuthError &e) {
        emit failed(QString::fromUtf8(e.what()), true);
    } catch (const std::exception &e) {
        if (!stop_.load())
            emit failed(QString::fromUtf8(e.what()), false);
    }
    secret_.fill(QChar(0));
    secret_.clear();
    emit state(QStringLiteral("已断开"));
}
} // namespace wise
