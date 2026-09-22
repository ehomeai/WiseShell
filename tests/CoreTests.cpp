#include "core/Credentials.h"
#include "core/Workspace.h"
#include "terminal/TerminalWidget.h"
#include <QApplication>
#include <QClipboard>
#include <QDir>
#include <QFile>
#include <QInputMethodEvent>
#include <QJsonDocument>
#include <QTemporaryDir>
#include <QtTest>
using namespace wise;
class CoreTests : public QObject {
    Q_OBJECT
  private slots:
    void repositoryRoundtrip() {
        QTemporaryDir dir;
        SessionRepository repo(dir.path());
        auto w = repo.load();
        QVERIFY(w.sessions.isEmpty());
        Profile p;
        p.name = QStringLiteral("中文服务器");
        p.host = "localhost";
        p.group = "work/dev";
        p.favorite = true;
        p.keyAuth = true;
        p.privateKey = "/key";
        p.terminal.theme = "Solarized";
        w.sessions.append(p);
        w.folders = {"empty", "work/dev"};
        repo.save(w);
        auto result = repo.load();
        QCOMPARE(result.sessions.first().json(), p.json());
        QCOMPARE(result.folders, w.folders);
        QVERIFY(QFile::exists(dir.filePath("sessions.json")));
    }
    void legacySessionsDefaultToRememberedCredentials() {
        QTemporaryDir dir;
        Profile profile;
        profile.remember = false;
        atomicJson(dir.filePath("sessions.json"),
                   {{"version", 1}, {"sessions", QJsonArray{profile.json()}}, {"folders", QJsonArray{}}});
        SessionRepository repo(dir.path());
        auto workspace = repo.load();
        QVERIFY(workspace.sessions.first().remember);
        repo.save(workspace);
        QFile saved(dir.filePath("sessions.json"));
        QVERIFY(saved.open(QIODevice::ReadOnly));
        QCOMPARE(QJsonDocument::fromJson(saved.readAll()).object().value("version").toInt(), 2);
    }
    void corruptConfigurationIsNotOverwritten() {
        QTemporaryDir dir;
        QFile f(dir.filePath("sessions.json"));
        QVERIFY(f.open(QIODevice::WriteOnly));
        f.write("broken");
        f.close();
        SessionRepository repo(dir.path());
        QVERIFY_EXCEPTION_THROWN(repo.load(), std::runtime_error);
        QVERIFY(f.open(QIODevice::ReadOnly));
        QCOMPARE(f.readAll(), QByteArray("broken"));
    }
    void groupsAndDefaults() {
        Workspace w;
        Profile p;
        p.group = "a/b";
        Profile custom;
        custom.terminal.fontSize = 25;
        w.sessions = {p, custom};
        w.folders = {"a/b", "a/empty"};
        moveGroup(w, "a", "new/a");
        QCOMPARE(w.sessions.first().group, QString("new/a/b"));
        QVERIFY(w.folders.contains("new/a/empty"));
        QVERIFY_EXCEPTION_THROWN(moveGroup(w, "new", "new/a/loop"), std::runtime_error);
        auto s = w.defaults;
        s.fontSize = 20;
        s.theme = "Light";
        applyDefaults(w, s, true);
        QCOMPARE(w.sessions[0].terminal.fontSize, 20.);
        QCOMPARE(w.sessions[1].terminal.fontSize, 25.);
        QCOMPARE(normalizeGroup(" / a \\ b //"), QString("a/b"));
    }
    void hostKeyChanges() {
        QTemporaryDir dir;
        HostKeyTrustStore store(dir.path());
        HostKey h{"server", 22, "ssh-ed25519", "SHA256:abc"};
        QCOMPARE(store.check(h), Trust::Unknown);
        store.trust(h);
        QCOMPARE(store.check(h), Trust::Match);
        h.fingerprint = "SHA256:def";
        QCOMPARE(store.check(h), Trust::Changed);
        store.trust(h);
        QCOMPARE(store.check(h), Trust::Match);
        h.fingerprint = "SHA256:abc";
        QCOMPARE(store.check(h), Trust::Changed);
        h.port = 2222;
        QCOMPARE(store.check(h), Trust::Unknown);
    }
    void pathsAndExclusions() {
        QCOMPARE(remoteJoin("/home/a", "../b"), QString("/home/b"));
        QCOMPARE(remoteJoin("/a", "/b"), QString("/b"));
        for (auto n : {".git", ".SVN", "node_modules", "obj", "TestResults"})
            QVERIFY(excludedUpload(n, true));
        for (auto n : {"x.log", "x.suo", "Thumbs.db"})
            QVERIFY(excludedUpload(n, false));
        QVERIFY(!excludedUpload("src", true));
        QVERIFY(!excludedUpload("main.cpp", false));
    }
    void dataDirectoryCanBeIsolated() {
        QTemporaryDir dir;
        const auto original = qgetenv("WISESHELL_DATA_DIRECTORY");
        qputenv("WISESHELL_DATA_DIRECTORY", dir.path().toUtf8());
        QCOMPARE(dataDirectory(), QDir::cleanPath(dir.path()));
        if (original.isEmpty())
            qunsetenv("WISESHELL_DATA_DIRECTORY");
        else
            qputenv("WISESHELL_DATA_DIRECTORY", original);
    }
    void secretCache() {
        Credentials credentials;
        credentials.cache("id", "secret");
        QCOMPARE(credentials.runtime("id"), QString("secret"));
        credentials.forgetRuntime("id");
        QVERIFY(credentials.runtime("id").isEmpty());
        credentials.invalidate("id");
        bool completed = false;
        credentials.read("id", [&](QString secret, QString error) {
            QVERIFY(secret.isEmpty());
            QVERIFY(error.isEmpty());
            completed = true;
        });
        QVERIFY(completed);
    }
    void systemCredentialRoundtrip() {
        if (qEnvironmentVariableIsEmpty("WISESHELL_TEST_KEYCHAIN"))
            QSKIP("Set WISESHELL_TEST_KEYCHAIN=1 for a real system credential-store roundtrip");
        const auto id = "test-" + QUuid::createUuid().toString(QUuid::WithoutBraces);
        Credentials writer, reader;
        bool complete = false;
        QString error, value;
        writer.write(id, "test-secret-value", [&](QString, QString e) {
            error = e;
            complete = true;
        });
        QTRY_VERIFY_WITH_TIMEOUT(complete, 10000);
        QVERIFY2(error.isEmpty(), qPrintable(error));
        complete = false;
        reader.read(id, [&](QString s, QString e) {
            value = s;
            error = e;
            complete = true;
        });
        QTRY_VERIFY_WITH_TIMEOUT(complete, 10000);
        QCOMPARE(value, QString("test-secret-value"));
        QVERIFY(error.isEmpty());
        complete = false;
        writer.remove(id, [&](QString, QString e) {
            error = e;
            complete = true;
        });
        QTRY_VERIFY_WITH_TIMEOUT(complete, 10000);
        QVERIFY(error.isEmpty());
    }
    void terminalUtf8AndControl() {
        TerminalWidget w;
        w.resize(850, 400);
        w.show();
        QTest::qWait(10);
        auto bytes = QStringLiteral("你好世界").toUtf8();
        w.feed(bytes.first(2));
        w.feed(bytes.mid(2));
        QVERIFY(w.screenText().startsWith(QStringLiteral("你好世界")));
        w.feed("\r\033[2K\033[31mRED\033[0m");
        QVERIFY(w.screenText().startsWith("RED"));
        w.feed("\033[?1049hALT");
        QVERIFY(w.screenText().contains("ALT"));
        w.feed("\033[?1049l");
        QVERIFY(w.screenText().startsWith("RED"));
    }
    void terminalInputAndPaste() {
        TerminalWidget w;
        QSignalSpy spy(&w, &TerminalWidget::input);
        QTest::keyClick(&w, Qt::Key_Tab);
        QVERIFY(!spy.isEmpty());
        QCOMPARE(spy.takeFirst().at(0).toByteArray(), QByteArray("\t"));
        QTest::keyClick(&w, Qt::Key_C, Qt::ControlModifier);
        QVERIFY(!spy.isEmpty());
        QCOMPARE(spy.takeFirst().at(0).toByteArray(), QByteArray(1, char(3)));
        QTest::keyClick(&w, Qt::Key_Up);
        QVERIFY(!spy.isEmpty());
        QCOMPARE(spy.takeFirst().at(0).toByteArray(), QByteArray("\033[A"));
        QInputMethodEvent event;
        event.setCommitString(QStringLiteral("输入"));
        QApplication::sendEvent(&w, &event);
        QByteArray all;
        for (const auto &args : spy)
            all += args[0].toByteArray();
        QCOMPARE(QString::fromUtf8(all), QStringLiteral("输入"));
        spy.clear();
        QApplication::clipboard()->setText("a\nb");
        QTest::keyClick(&w, Qt::Key_Insert, Qt::ControlModifier);
        QVERIFY(spy.isEmpty());
        QCOMPARE(QApplication::clipboard()->text(), QString("a\nb"));
        w.feed("\033[?2004h");
        QTest::keyClick(&w, Qt::Key_Insert, Qt::ShiftModifier);
        all.clear();
        for (const auto &args : spy)
            all += args[0].toByteArray();
        QCOMPARE(all, QByteArray("\033[200~a\rb\033[201~"));
    }
    void terminalScrollAndResize() {
        TerminalWidget w;
        TerminalSettings s;
        s.scrollback = 8;
        w.setSettings(s);
        w.resize(500, 180);
        w.show();
        QTest::qWait(10);
        QSignalSpy resized(&w, &TerminalWidget::terminalResized);
        for (int i = 0; i < 100; ++i)
            w.feed("line\r\n");
        QCOMPARE(w.historyLines(), 8);
        w.resize(750, 300);
        QTest::qWait(10);
        QVERIFY(!resized.isEmpty());
        QVERIFY(w.screenText().contains("line"));
    }
};
QTEST_MAIN(CoreTests)
#include "CoreTests.moc"
