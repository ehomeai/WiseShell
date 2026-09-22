#pragma once
#include "core/Workspace.h"
#include <QByteArray>
#include <QMutex>
#include <QWaitCondition>
#include <atomic>
#include <libssh/libssh.h>
#include <memory>

namespace wise {
struct Decision {
    QMutex mutex;
    QWaitCondition changed;
    int answer = -1;
    void resolve(int value) {
        QMutexLocker lock(&mutex);
        if (answer < 0)
            answer = value;
        changed.wakeAll();
    }
    int wait(const std::atomic_bool &cancel) {
        QMutexLocker lock(&mutex);
        while (answer < 0 && !cancel.load())
            changed.wait(&mutex, 100);
        return cancel.load() ? 0 : answer;
    }
};
using DecisionPtr = std::shared_ptr<Decision>;
using VerifyHost = std::function<bool(const HostKey &)>;
struct AuthError : std::runtime_error {
    using std::runtime_error::runtime_error;
};
class Connection {
  public:
    Connection() = default;
    ~Connection();
    Connection(const Connection &) = delete;
    Connection &operator=(const Connection &) = delete;
    void connect(const Profile &, const QString &secret, const VerifyHost &, const std::atomic_bool &cancel);
    ssh_session get() const {
        return session_;
    }
    QString error() const;

  private:
    ssh_session session_ = nullptr;
};
} // namespace wise
Q_DECLARE_METATYPE(wise::DecisionPtr)
