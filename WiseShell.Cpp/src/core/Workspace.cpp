#include "Workspace.h"
#include <QDir>
#include <QFile>
#include <QJsonArray>
#include <QJsonDocument>
#include <QSaveFile>
#include <QSet>
#include <QStandardPaths>
#include <algorithm>

namespace wise {
static QJsonObject readJson(const QString &path) {
    QFile file(path);
    if (!file.exists())
        return {};
    if (!file.open(QIODevice::ReadOnly))
        throw std::runtime_error(file.errorString().toStdString());
    QJsonParseError error;
    const auto doc = QJsonDocument::fromJson(file.readAll(), &error);
    if (error.error != QJsonParseError::NoError || !doc.isObject())
        throw std::runtime_error((QStringLiteral("配置文件损坏：") + path).toStdString());
    return doc.object();
}
void atomicJson(const QString &path, const QJsonObject &object) {
    if (!QDir().mkpath(QFileInfo(path).absolutePath()))
        throw std::runtime_error("Cannot create data directory");
    QSaveFile file(path);
    const auto bytes = QJsonDocument(object).toJson();
    if (!file.open(QIODevice::WriteOnly) || file.write(bytes) != bytes.size() || !file.commit())
        throw std::runtime_error(file.errorString().toStdString());
}
QString dataDirectory() {
    // Test and portable launches can opt into an isolated application-data
    // directory without changing the normal per-user location.
    const auto override = qEnvironmentVariable("WISESHELL_DATA_DIRECTORY").trimmed();
    if (!override.isEmpty())
        return QDir::cleanPath(override);
    return QStandardPaths::writableLocation(QStandardPaths::AppDataLocation);
}
QJsonObject TerminalSettings::json() const {
    return {{"theme", theme}, {"fontFamily", fontFamily}, {"fontSize", fontSize}, {"scrollback", scrollback}};
}
TerminalSettings TerminalSettings::fromJson(const QJsonObject &o) {
    TerminalSettings s;
    s.theme = o.value("theme").toString(s.theme);
    s.fontFamily = o.value("fontFamily").toString(s.fontFamily);
    s.fontSize = std::clamp(o.value("fontSize").toDouble(16), 9., 72.);
    s.scrollback = std::clamp(o.value("scrollback").toInt(5000), 0, 100000);
    if (s.fontFamily.trimmed().isEmpty())
        s.fontFamily = "DejaVu Sans Mono";
    if (!QStringList{"Dark", "Light", "Solarized"}.contains(s.theme))
        s.theme = "Dark";
    return s;
}
QJsonObject Profile::json() const {
    return {{"id", id},
            {"name", name},
            {"group", group},
            {"host", host},
            {"port", port},
            {"username", username},
            {"keyAuth", keyAuth},
            {"privateKey", privateKey},
            {"localDirectory", localDirectory},
            {"remoteDirectory", remoteDirectory},
            {"keepAlive", keepAlive},
            {"favorite", favorite},
            {"remember", remember},
            {"terminal", terminal.json()}};
}
Profile Profile::fromJson(const QJsonObject &o) {
    Profile p;
    p.id = o.value("id").toString(p.id);
    p.name = o.value("name").toString(p.name);
    p.group = normalizeGroup(o.value("group").toString());
    p.host = o.value("host").toString();
    p.port = std::clamp(o.value("port").toInt(22), 1, 65535);
    p.username = o.value("username").toString();
    p.keyAuth = o.value("keyAuth").toBool();
    p.privateKey = o.value("privateKey").toString();
    p.localDirectory = o.value("localDirectory").toString();
    p.remoteDirectory = o.value("remoteDirectory").toString();
    p.keepAlive = std::clamp(o.value("keepAlive").toInt(30), 0, 3600);
    p.favorite = o.value("favorite").toBool();
    p.remember = !o.contains("remember") || o.value("remember").toBool();
    p.terminal = TerminalSettings::fromJson(o.value("terminal").toObject());
    return p;
}
SessionRepository::SessionRepository(QString directory) : path_(QDir(directory).filePath("sessions.json")) {}
Workspace SessionRepository::load() {
    const auto o = readJson(path_);
    Workspace w;
    const int version = o.isEmpty() ? 2 : o.value("version").toInt();
    if (version != 1 && version != 2)
        throw std::runtime_error("Unsupported configuration version");
    w.defaults = TerminalSettings::fromJson(o.value("defaults").toObject());
    QSet<QString> ids;
    for (const auto &v : o.value("sessions").toArray()) {
        auto p = Profile::fromJson(v.toObject());
        // Version 1 predated automatic secure credential storage.  Existing
        // sessions now use the requested default, while version 2 preserves a
        // user's later decision to turn it off.
        if (version == 1)
            p.remember = true;
        if (QUuid(p.id).isNull() || ids.contains(p.id))
            throw std::runtime_error("Invalid or duplicate session ID");
        ids.insert(p.id);
        w.sessions.append(p);
    }
    for (const auto &v : o.value("folders").toArray()) {
        auto s = normalizeGroup(v.toString());
        if (!s.isEmpty())
            w.folders.append(s);
    }
    w.folders.removeDuplicates();
    return w;
}
void SessionRepository::save(const Workspace &w) {
    QJsonArray sessions, folders;
    for (const auto &p : w.sessions)
        sessions.append(p.json());
    for (const auto &s : w.folders)
        folders.append(normalizeGroup(s));
    atomicJson(
        path_,
        {{"version", 2}, {"sessions", sessions}, {"folders", folders}, {"defaults", w.defaults.json()}});
}
HostKeyTrustStore::HostKeyTrustStore(QString directory)
    : path_(QDir(directory).filePath("known-hosts.json")) {}
Trust HostKeyTrustStore::check(const HostKey &h) {
    bool found = false;
    for (const auto &v : readJson(path_).value("hosts").toArray()) {
        const auto o = v.toObject();
        if (o.value("host").toString().compare(h.host, Qt::CaseInsensitive) == 0 &&
            o.value("port").toInt() == h.port) {
            found = true;
            if (o.value("algorithm").toString() == h.algorithm &&
                o.value("fingerprint").toString() == h.fingerprint)
                return Trust::Match;
        }
    }
    return found ? Trust::Changed : Trust::Unknown;
}
void HostKeyTrustStore::trust(const HostKey &h) {
    QJsonArray a;
    for (const auto &v : readJson(path_).value("hosts").toArray()) {
        const auto o = v.toObject();
        if (o.value("host").toString().compare(h.host, Qt::CaseInsensitive) != 0 ||
            o.value("port").toInt() != h.port)
            a.append(v);
    }
    a.append(QJsonObject{
        {"host", h.host}, {"port", h.port}, {"algorithm", h.algorithm}, {"fingerprint", h.fingerprint}});
    atomicJson(path_, {{"hosts", a}});
}
QString normalizeGroup(QString path) {
    path.replace('\\', '/');
    QStringList parts;
    for (auto s : path.split('/', Qt::SkipEmptyParts)) {
        s = s.trimmed();
        if (!s.isEmpty() && s != "." && s != "..")
            parts.append(s);
    }
    return parts.join('/');
}
QString remoteJoin(const QString &base, const QString &name) {
    if (name.startsWith('/'))
        return QDir::cleanPath(name);
    return QDir::cleanPath((base.isEmpty() ? QString(".") : base) + "/" + name);
}
bool excludedUpload(const QString &name, bool directory) {
    const auto lower = name.toLower();
    static const QSet<QString> dirs = {".svn",     ".git",        ".hg",      ".vs",       ".vscode",
                                       ".idea",    "bin",         "obj",      "artifacts", "node_modules",
                                       "packages", "testresults", "ebwebview"};
    if (directory)
        return dirs.contains(lower);
    if (lower == "thumbs.db" || lower == "desktop.ini")
        return true;
    for (const auto &s :
         QStringList{".log", ".tmp", ".temp", ".user", ".suo", ".userosscache", ".sln.docstates"})
        if (lower.endsWith(s))
            return true;
    return false;
}
void moveGroup(Workspace &w, const QString &oldPath, const QString &newPath) {
    const auto old = normalizeGroup(oldPath), next = normalizeGroup(newPath);
    if (old.isEmpty() || next.isEmpty() || next.startsWith(old + "/") || old == next)
        throw std::runtime_error("Invalid group move");
    auto replace = [&](QString &s) {
        if (s == old || s.startsWith(old + "/"))
            s = next + s.mid(old.size());
    };
    for (auto &s : w.folders)
        replace(s);
    for (auto &p : w.sessions)
        replace(p.group);
    w.folders.append(next);
    w.folders.removeDuplicates();
}
void applyDefaults(Workspace &w, const TerminalSettings &next, bool apply) {
    if (apply)
        for (auto &p : w.sessions) {
            if (p.terminal.fontFamily == w.defaults.fontFamily)
                p.terminal.fontFamily = next.fontFamily;
            if (p.terminal.fontSize == w.defaults.fontSize)
                p.terminal.fontSize = next.fontSize;
            if (p.terminal.theme == w.defaults.theme)
                p.terminal.theme = next.theme;
            if (p.terminal.scrollback == w.defaults.scrollback)
                p.terminal.scrollback = next.scrollback;
        }
    w.defaults = next;
}
} // namespace wise
