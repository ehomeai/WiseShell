#pragma once
#include "Connection.h"
#include <QDateTime>
#include <QElapsedTimer>
#include <QQueue>
#include <QThread>
#include <libssh/sftp.h>
namespace wise {
struct RemoteEntry {
    QString name, path;
    quint64 size = 0;
    QDateTime modified;
    bool directory = false, symlink = false;
};
enum class FileOperation { List, Upload, Download, Remove, Mkdir, Rename };
struct FileTask {
    QString id = QUuid::createUuid().toString(QUuid::WithoutBraces);
    FileOperation operation = FileOperation::List;
    QString source, destination;
};
class ISftpSession {
  public:
    virtual ~ISftpSession() = default;
    virtual void enqueue(const FileTask &) = 0;
    virtual void cancel(const QString &id) = 0;
    virtual void close() = 0;
};
class SftpSession final : public QThread, public ISftpSession {
    Q_OBJECT
  public:
    SftpSession(Profile, QString secret, QObject *parent = nullptr);
    ~SftpSession() override;
    void enqueue(const FileTask &) override;
    void cancel(const QString &) override;
    void close() override;
  signals:
    void verifyHost(wise::HostKey, wise::DecisionPtr);
    void conflict(QString path, wise::DecisionPtr);
    void state(QString);
    void failed(QString, bool authentication);
    void listed(QString path, QList<wise::RemoteEntry> entries);
    void taskStarted(QString id);
    void taskFinished(QString id, QString result, bool success);
    void progress(QString id, quint64 done, quint64 total, double bytesPerSecond);

  protected:
    void run() override;

  private:
    void checkCancel();
    QList<RemoteEntry> list(const QString &);
    void upload(const QString &, const QString &, int depth = 0);
    void download(const QString &, const QString &, int depth = 0);
    void removeRemote(const QString &, int depth = 0);
    quint64 localSize(const QString &, int depth = 0);
    quint64 remoteSize(const QString &, int depth = 0);
    bool overwrite(const QString &);
    void report(quint64 bytes);
    void ensureRemoteDirectory(const QString &);
    [[noreturn]] void error(const QString &operation);
    Profile profile_;
    QString secret_, activeId_;
    std::atomic_bool stop_{false}, cancelTask_{false};
    QMutex mutex_;
    QWaitCondition wake_;
    QQueue<FileTask> tasks_;
    sftp_session sftp_ = nullptr;
    quint64 done_ = 0, total_ = 0;
    QElapsedTimer elapsed_, progressClock_;
};
} // namespace wise
Q_DECLARE_METATYPE(wise::RemoteEntry)
Q_DECLARE_METATYPE(QList<wise::RemoteEntry>)
