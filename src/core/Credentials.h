#pragma once
#include <QHash>
#include <QObject>
#include <QSet>
#include <functional>

namespace wise {
using SecretResult = std::function<void(QString secret, QString error)>;
class ICredentialStore {
  public:
    virtual ~ICredentialStore() = default;
    virtual void read(const QString &, SecretResult) = 0;
    virtual void write(const QString &, const QString &, SecretResult) = 0;
    virtual void remove(const QString &, SecretResult) = 0;
};
class Credentials final : public QObject, public ICredentialStore {
    Q_OBJECT
  public:
    using QObject::QObject;
    void read(const QString &, SecretResult) override;
    void write(const QString &, const QString &, SecretResult) override;
    void remove(const QString &, SecretResult) override;
    void forgetRuntime(const QString &id) {
        cache_.remove(id);
    }
    void invalidate(const QString &id);
    QString runtime(const QString &id) const {
        return cache_.value(id);
    }
    void cache(const QString &id, const QString &secret) {
        cache_[id] = secret;
    }

  private:
    QHash<QString, QString> cache_;
    QSet<QString> invalid_;
};
} // namespace wise
