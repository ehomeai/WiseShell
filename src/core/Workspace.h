#pragma once
#include <QJsonObject>
#include <QList>
#include <QStringList>
#include <QUuid>
#include <functional>
#include <stdexcept>

namespace wise {
struct TerminalSettings {
    QString theme = "Dark", fontFamily = "DejaVu Sans Mono";
    double fontSize = 16;
    int scrollback = 5000;
    QJsonObject json() const;
    static TerminalSettings fromJson(const QJsonObject &);
    bool operator==(const TerminalSettings &) const = default;
};
struct Profile {
    QString id = QUuid::createUuid().toString(QUuid::WithoutBraces);
    QString name = QStringLiteral("新建会话"), group, host, username, privateKey, localDirectory,
            remoteDirectory;
    int port = 22, keepAlive = 30;
    bool keyAuth = false, favorite = false, remember = true;
    TerminalSettings terminal;
    QJsonObject json() const;
    static Profile fromJson(const QJsonObject &);
};
struct Workspace {
    QList<Profile> sessions;
    QStringList folders;
    TerminalSettings defaults;
};
class ISessionRepository {
  public:
    virtual ~ISessionRepository() = default;
    virtual Workspace load() = 0;
    virtual void save(const Workspace &) = 0;
};
class SessionRepository final : public ISessionRepository {
  public:
    explicit SessionRepository(QString directory);
    Workspace load() override;
    void save(const Workspace &) override;

  private:
    QString path_;
};
struct HostKey {
    QString host;
    int port = 22;
    QString algorithm, fingerprint;
};
enum class Trust { Unknown, Match, Changed };
class IHostKeyTrustStore {
  public:
    virtual ~IHostKeyTrustStore() = default;
    virtual Trust check(const HostKey &) = 0;
    virtual void trust(const HostKey &) = 0;
};
class HostKeyTrustStore final : public IHostKeyTrustStore {
  public:
    explicit HostKeyTrustStore(QString directory);
    Trust check(const HostKey &) override;
    void trust(const HostKey &) override;

  private:
    QString path_;
};
QString dataDirectory();
QString normalizeGroup(QString);
QString remoteJoin(const QString &, const QString &);
bool excludedUpload(const QString &name, bool directory);
void moveGroup(Workspace &, const QString &oldPath, const QString &newPath);
void applyDefaults(Workspace &, const TerminalSettings &, bool applyToInherited);
void atomicJson(const QString &path, const QJsonObject &);
} // namespace wise
Q_DECLARE_METATYPE(wise::Profile)
Q_DECLARE_METATYPE(wise::HostKey)
