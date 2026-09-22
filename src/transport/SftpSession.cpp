#include "SftpSession.h"
#include <QDir>
#include <QFile>
#include <QFileInfo>
#include <QSaveFile>
#include <algorithm>
#include <fcntl.h>
namespace wise {
namespace {
struct Cancelled : std::runtime_error {
    Cancelled() : std::runtime_error("Cancelled") {}
};
using Attr = std::unique_ptr<sftp_attributes_struct, decltype(&sftp_attributes_free)>;
using RemoteFile = std::unique_ptr<sftp_file_struct, decltype(&sftp_close)>;
using RemoteDir = std::unique_ptr<sftp_dir_struct, decltype(&sftp_closedir)>;
bool validName(const QString &name) {
#ifdef Q_OS_WIN
    if (name.contains(':') || name.endsWith('.') || name.endsWith(' '))
        return false;
    const auto stem = name.section('.', 0, 0).toUpper();
    if (QStringList{"CON",  "PRN",  "AUX",  "NUL",  "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7",
                    "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"}
            .contains(stem))
        return false;
#endif
    return !name.isEmpty() && name != "." && name != ".." && !name.contains('/') && !name.contains('\\') &&
           !name.contains(QChar(0));
}
} // namespace
SftpSession::SftpSession(Profile p, QString secret, QObject *parent)
    : QThread(parent), profile_(std::move(p)), secret_(std::move(secret)) {}
SftpSession::~SftpSession() {
    close();
    wait();
}
void SftpSession::enqueue(const FileTask &task) {
    QMutexLocker lock(&mutex_);
    tasks_.enqueue(task);
    wake_.wakeAll();
}
void SftpSession::cancel(const QString &id) {
    bool removed = false;
    {
        QMutexLocker lock(&mutex_);
        if (activeId_ == id)
            cancelTask_ = true;
        else
            for (auto it = tasks_.begin(); it != tasks_.end(); ++it)
                if (it->id == id) {
                    tasks_.erase(it);
                    removed = true;
                    break;
                }
    }
    if (removed)
        emit taskFinished(id, QStringLiteral("已取消"), false);
}
void SftpSession::close() {
    stop_ = true;
    cancelTask_ = true;
    wake_.wakeAll();
}
void SftpSession::checkCancel() {
    if (stop_.load() || cancelTask_.load())
        throw Cancelled();
}
void SftpSession::error(const QString &operation) {
    throw std::runtime_error((operation + ": " + QString::fromUtf8(ssh_get_error(sftp_->session)) +
                              QString(" (SFTP %1)").arg(sftp_get_error(sftp_)))
                                 .toStdString());
}
QList<RemoteEntry> SftpSession::list(const QString &path) {
    checkCancel();
    RemoteDir dir(sftp_opendir(sftp_, path.toUtf8().constData()), sftp_closedir);
    if (!dir)
        error("List " + path);
    QList<RemoteEntry> entries;
    for (;;) {
        checkCancel();
        Attr attr(sftp_readdir(sftp_, dir.get()), sftp_attributes_free);
        if (!attr) {
            if (!sftp_dir_eof(dir.get()))
                error("Read directory " + path);
            break;
        }
        const QString name = QString::fromUtf8(attr->name);
        if (name == "." || name == "..")
            continue;
        if (!validName(name))
            throw std::runtime_error("Remote filename cannot be represented safely on this platform");
        entries.append({name,
                        remoteJoin(path, name),
                        attr->size,
                        QDateTime::fromSecsSinceEpoch(qint64(attr->mtime)),
                        attr->type == SSH_FILEXFER_TYPE_DIRECTORY,
                        attr->type == SSH_FILEXFER_TYPE_SYMLINK});
    }
    std::sort(entries.begin(), entries.end(), [](const auto &a, const auto &b) {
        if (a.directory != b.directory)
            return a.directory;
        return a.name.localeAwareCompare(b.name) < 0;
    });
    return entries;
}
quint64 SftpSession::localSize(const QString &path, int depth) {
    checkCancel();
    if (depth > 128)
        throw std::runtime_error("Directory nesting exceeds 128");
    QFileInfo info(path);
    if (info.isSymLink() || excludedUpload(info.fileName(), info.isDir()))
        return 0;
    if (!info.exists())
        throw std::runtime_error("Local source does not exist");
    if (!info.isDir() && !info.isFile())
        throw std::runtime_error("Unsupported local file type");
    if (!info.isDir())
        return quint64(info.size());
    quint64 size = 0;
    for (const auto &i :
         QDir(path).entryInfoList(QDir::AllEntries | QDir::NoDotAndDotDot | QDir::Hidden | QDir::System))
        size += localSize(i.absoluteFilePath(), depth + 1);
    return size;
}
quint64 SftpSession::remoteSize(const QString &path, int depth) {
    checkCancel();
    if (depth > 128)
        throw std::runtime_error("Directory nesting exceeds 128");
    Attr a(sftp_lstat(sftp_, path.toUtf8().constData()), sftp_attributes_free);
    if (!a)
        error("Stat " + path);
    if (a->type == SSH_FILEXFER_TYPE_SYMLINK)
        return 0;
    if (a->type != SSH_FILEXFER_TYPE_DIRECTORY)
        return a->size;
    quint64 size = 0;
    for (const auto &e : list(path))
        size += remoteSize(e.path, depth + 1);
    return size;
}
bool SftpSession::overwrite(const QString &path) {
    auto d = std::make_shared<Decision>();
    emit conflict(path, d);
    // Poll both connection and task cancellation while the UI is deciding.
    QMutexLocker lock(&d->mutex);
    while (d->answer < 0 && !stop_.load() && !cancelTask_.load())
        d->changed.wait(&d->mutex, 100);
    checkCancel();
    if (d->answer == 0)
        throw Cancelled();
    return d->answer == 1;
}
void SftpSession::report(quint64 bytes) {
    done_ += bytes;
    if (progressClock_.elapsed() < 80 && done_ < total_)
        return;
    progressClock_.restart();
    emit progress(activeId_, done_, total_,
                  double(done_) * 1000. / double(std::max<qint64>(1, elapsed_.elapsed())));
}
void SftpSession::ensureRemoteDirectory(const QString &path) {
    Attr a(sftp_lstat(sftp_, path.toUtf8().constData()), sftp_attributes_free);
    if (a) {
        if (a->type != SSH_FILEXFER_TYPE_DIRECTORY)
            throw std::runtime_error("Destination is not a directory (or is a symbolic link)");
        return;
    }
    if (sftp_get_error(sftp_) != SSH_FX_NO_SUCH_FILE)
        error("Stat " + path);
    if (sftp_mkdir(sftp_, path.toUtf8().constData(), 0755) != SSH_OK)
        error("Mkdir " + path);
}
void SftpSession::upload(const QString &source, const QString &dest, int depth) {
    checkCancel();
    if (depth > 128)
        throw std::runtime_error("Directory nesting exceeds 128");
    QFileInfo info(source);
    if (info.isSymLink() || excludedUpload(info.fileName(), info.isDir()))
        return;
    if (info.isDir()) {
        ensureRemoteDirectory(dest);
        for (const auto &i : QDir(source).entryInfoList(QDir::AllEntries | QDir::NoDotAndDotDot |
                                                        QDir::Hidden | QDir::System))
            upload(i.absoluteFilePath(), remoteJoin(dest, i.fileName()), depth + 1);
        return;
    }
    Attr exists(sftp_lstat(sftp_, dest.toUtf8().constData()), sftp_attributes_free);
    if (exists) {
        if (exists->type != SSH_FILEXFER_TYPE_REGULAR)
            throw std::runtime_error("Refusing to overwrite directory or symbolic link");
        if (!overwrite(dest)) {
            report(quint64(info.size()));
            return;
        }
    } else if (sftp_get_error(sftp_) != SSH_FX_NO_SUCH_FILE)
        error("Stat " + dest);
    QFile local(source);
    if (!local.open(QIODevice::ReadOnly))
        throw std::runtime_error(local.errorString().toStdString());
    // Exclusive staging file prevents a cancelled transfer from truncating the destination.
    const QString temp = dest + ".wiseshell-" + QUuid::createUuid().toString(QUuid::WithoutBraces) + ".part";
    try {
        RemoteFile file(sftp_open(sftp_, temp.toUtf8().constData(), O_WRONLY | O_CREAT | O_EXCL, 0600),
                        sftp_close);
        if (!file)
            error("Open " + temp);
        while (!local.atEnd()) {
            checkCancel();
            const auto bytes = local.read(64 * 1024);
            if (bytes.isEmpty() && local.error() != QFile::NoError)
                throw std::runtime_error(local.errorString().toStdString());
            qsizetype offset = 0;
            while (offset < bytes.size()) {
                checkCancel();
                auto n = sftp_write(file.get(), bytes.constData() + offset, size_t(bytes.size() - offset));
                if (n <= 0)
                    error("Write " + temp);
                offset += n;
                report(quint64(n));
            }
        }
        auto raw = file.release();
        if (sftp_close(raw) != SSH_OK)
            error("Close " + temp);
        checkCancel();
        // libssh uses the OpenSSH POSIX rename extension when available.
        if (sftp_rename(sftp_, temp.toUtf8().constData(), dest.toUtf8().constData()) != SSH_OK)
            error("Rename staged upload " + dest);
    } catch (...) {
        sftp_unlink(sftp_, temp.toUtf8().constData());
        throw;
    }
}
void SftpSession::download(const QString &source, const QString &dest, int depth) {
    checkCancel();
    if (depth > 128)
        throw std::runtime_error("Directory nesting exceeds 128");
    Attr a(sftp_lstat(sftp_, source.toUtf8().constData()), sftp_attributes_free);
    if (!a)
        error("Stat " + source);
    if (a->type == SSH_FILEXFER_TYPE_SYMLINK)
        return;
    QFileInfo target(dest);
    if (target.isSymLink())
        throw std::runtime_error("Refusing to follow local symbolic link");
    if (a->type == SSH_FILEXFER_TYPE_DIRECTORY) {
        if (!QDir().mkpath(dest))
            throw std::runtime_error("Cannot create local directory");
        for (const auto &e : list(source))
            download(e.path, QDir(dest).filePath(e.name), depth + 1);
        return;
    }
    if (a->type != SSH_FILEXFER_TYPE_REGULAR)
        throw std::runtime_error("Unsupported remote file type");
    if (target.exists()) {
        if (!target.isFile())
            throw std::runtime_error("Destination is not a regular file");
        if (!overwrite(dest)) {
            report(a->size);
            return;
        }
    }
    QSaveFile local(dest);
    if (!local.open(QIODevice::WriteOnly))
        throw std::runtime_error(local.errorString().toStdString());
    RemoteFile file(sftp_open(sftp_, source.toUtf8().constData(), O_RDONLY, 0), sftp_close);
    if (!file)
        error("Open " + source);
    for (;;) {
        checkCancel();
        char bytes[64 * 1024];
        auto n = sftp_read(file.get(), bytes, sizeof bytes);
        if (n == 0)
            break;
        if (n < 0)
            error("Read " + source);
        if (local.write(bytes, n) != n)
            throw std::runtime_error(local.errorString().toStdString());
        report(quint64(n));
    }
    checkCancel();
    if (!local.commit())
        throw std::runtime_error(local.errorString().toStdString());
}
void SftpSession::removeRemote(const QString &path, int depth) {
    checkCancel();
    if (depth > 128)
        throw std::runtime_error("Directory nesting exceeds 128");
    if (path.isEmpty() || QDir::cleanPath(path) == "/" || path == "." || path == "..")
        throw std::runtime_error("Refusing to delete remote root");
    Attr a(sftp_lstat(sftp_, path.toUtf8().constData()), sftp_attributes_free);
    if (!a)
        error("Stat " + path);
    if (a->type == SSH_FILEXFER_TYPE_DIRECTORY) {
        for (const auto &e : list(path))
            removeRemote(e.path, depth + 1);
        if (sftp_rmdir(sftp_, path.toUtf8().constData()) != SSH_OK)
            error("Rmdir " + path);
    } else if (sftp_unlink(sftp_, path.toUtf8().constData()) != SSH_OK)
        error("Delete " + path);
}
void SftpSession::run() {
    emit state(QStringLiteral("连接中"));
    try {
        Connection connection;
        connection.connect(
            profile_, secret_,
            [this](const HostKey &key) {
                auto d = std::make_shared<Decision>();
                emit verifyHost(key, d);
                return d->wait(stop_) == 1;
            },
            stop_);
        secret_.fill(QChar(0));
        secret_.clear();
        auto handle =
            std::unique_ptr<sftp_session_struct, decltype(&sftp_free)>(sftp_new(connection.get()), sftp_free);
        sftp_ = handle.get();
        if (!sftp_ || sftp_init(sftp_) != SSH_OK)
            throw std::runtime_error(connection.error().toStdString());
        emit state(QStringLiteral("已连接"));
        char *canonical = sftp_canonicalize_path(
            sftp_, profile_.remoteDirectory.isEmpty() ? "." : profile_.remoteDirectory.toUtf8().constData());
        if (canonical) {
            FileTask t;
            t.source = QString::fromUtf8(canonical);
            ssh_string_free_char(canonical);
            enqueue(t);
        } else {
            FileTask t;
            t.source = ".";
            enqueue(t);
        }
        QElapsedTimer keepAlive;
        keepAlive.start();
        while (!stop_.load()) {
            FileTask task;
            bool available = false;
            {
                QMutexLocker lock(&mutex_);
                if (tasks_.isEmpty())
                    wake_.wait(&mutex_, 250);
                if (!tasks_.isEmpty()) {
                    task = tasks_.dequeue();
                    activeId_ = task.id;
                    cancelTask_ = false;
                    available = true;
                }
            }
            if (!ssh_is_connected(connection.get()))
                throw std::runtime_error("SFTP connection lost");
            if (!available) {
                if (profile_.keepAlive > 0 && keepAlive.elapsed() >= profile_.keepAlive * 1000LL) {
                    ssh_send_ignore(connection.get(), "");
                    keepAlive.restart();
                }
                continue;
            }
            emit taskStarted(task.id);
            elapsed_.start();
            progressClock_.start();
            done_ = total_ = 0;
            try {
                checkCancel();
                switch (task.operation) {
                case FileOperation::List:
                    emit listed(task.source, list(task.source));
                    break;
                case FileOperation::Upload:
                    total_ = localSize(task.source);
                    upload(task.source, task.destination);
                    break;
                case FileOperation::Download:
                    total_ = remoteSize(task.source);
                    download(task.source, task.destination);
                    break;
                case FileOperation::Remove:
                    removeRemote(task.source);
                    break;
                case FileOperation::Mkdir:
                    ensureRemoteDirectory(task.source);
                    break;
                case FileOperation::Rename:
                    if (sftp_rename(sftp_, task.source.toUtf8().constData(),
                                    task.destination.toUtf8().constData()) != SSH_OK)
                        error("Rename " + task.source);
                    break;
                }
                report(0);
                emit taskFinished(task.id, QStringLiteral("已完成"), true);
            } catch (const Cancelled &) {
                emit taskFinished(task.id, QStringLiteral("已取消"), false);
            } catch (const std::exception &e) {
                emit taskFinished(task.id, QString::fromUtf8(e.what()), false);
            }
            {
                QMutexLocker lock(&mutex_);
                activeId_.clear();
            }
        }
    } catch (const AuthError &e) {
        emit failed(QString::fromUtf8(e.what()), true);
    } catch (const std::exception &e) {
        if (!stop_.load())
            emit failed(QString::fromUtf8(e.what()), false);
    }
    sftp_ = nullptr;
    secret_.fill(QChar(0));
    secret_.clear();
    QQueue<FileTask> pending;
    {
        QMutexLocker lock(&mutex_);
        pending.swap(tasks_);
    }
    for (const auto &t : pending)
        emit taskFinished(t.id, QStringLiteral("连接已关闭"), false);
    emit state(QStringLiteral("已断开"));
}
} // namespace wise
