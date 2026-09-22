#include "transport/SftpSession.h"
#include "transport/TerminalSession.h"
#include <QDir>
#include <QFile>
#include <QTemporaryDir>
#include <QtTest>
using namespace wise;
class IntegrationTests : public QObject {
    Q_OBJECT
    Profile profile() const {
        Profile p;
        p.host = "127.0.0.1";
        p.port = qEnvironmentVariableIntValue("WISESHELL_TEST_SSH_PORT");
        p.username = qEnvironmentVariable("WISESHELL_TEST_SSH_USER");
        return p;
    }
    QString secret() const {
        return qEnvironmentVariable("WISESHELL_TEST_SSH_PASSWORD");
    }
  private slots:
    void initTestCase() {
        if (qEnvironmentVariableIsEmpty("WISESHELL_TEST_SSH_PORT"))
            QSKIP("Set WISESHELL_TEST_SSH_PORT/USER/PASSWORD for isolated OpenSSH integration tests");
        qRegisterMetaType<DecisionPtr>();
        qRegisterMetaType<HostKey>();
        qRegisterMetaType<QList<RemoteEntry>>();
    }
    void shellAndRejectedFingerprint() {
        TerminalSession s(profile(), secret());
        QSignalSpy out(&s, &TerminalSession::output);
        QSignalSpy failures(&s, &TerminalSession::failed);
        connect(&s, &TerminalSession::verifyHost, this, [](HostKey h, DecisionPtr d) {
            QVERIFY(h.fingerprint.startsWith("SHA256:"));
            d->resolve(1);
        });
        s.start();
        QTRY_VERIFY_WITH_TIMEOUT(!out.isEmpty() || !failures.isEmpty(), 15000);
        QVERIFY(failures.isEmpty());
        s.sendInput("printf 'WISESHELL_%s\\n' 'OK'\r");
        QTRY_VERIFY_WITH_TIMEOUT(([&] {
                                     QByteArray b;
                                     for (auto a : out)
                                         b += a[0].toByteArray();
                                     return b.contains("WISESHELL_OK");
                                 })(),
                                 10000);
        s.resizeTerminal(100, 30);
        QTest::qWait(100);
        s.sendInput("stty size\r");
        QTRY_VERIFY_WITH_TIMEOUT(([&] {
                                     QByteArray b;
                                     for (auto a : out)
                                         b += a[0].toByteArray();
                                     return b.contains("30 100");
                                 })(),
                                 10000);
        s.sendInput("exit\r");
        QTRY_VERIFY_WITH_TIMEOUT(!s.isRunning(), 10000);
        QVERIFY(s.wait(15000));
        TerminalSession reject(profile(), secret());
        QSignalSpy rejected(&reject, &TerminalSession::failed);
        connect(&reject, &TerminalSession::verifyHost, this, [](HostKey, DecisionPtr d) { d->resolve(0); });
        reject.start();
        QTRY_VERIFY_WITH_TIMEOUT(!rejected.isEmpty(), 15000);
        QVERIFY(!rejected.first()[1].toBool());
        reject.wait();
    }
    void badPassword() {
        TerminalSession s(profile(), "deliberately-wrong");
        QSignalSpy failed(&s, &TerminalSession::failed);
        connect(&s, &TerminalSession::verifyHost, this, [](HostKey, DecisionPtr d) { d->resolve(1); });
        s.start();
        QTRY_VERIFY_WITH_TIMEOUT(!failed.isEmpty(), 15000);
        QVERIFY(failed.first()[1].toBool());
        s.wait();
    }
    void encryptedPrivateKey() {
        if (qEnvironmentVariableIsEmpty("WISESHELL_TEST_SSH_KEY"))
            QSKIP("Set WISESHELL_TEST_SSH_KEY to an authorized encrypted test key");
        auto p = profile();
        p.keyAuth = true;
        p.privateKey = qEnvironmentVariable("WISESHELL_TEST_SSH_KEY");
        TerminalSession s(p, "test-key-passphrase");
        QSignalSpy out(&s, &TerminalSession::output), failure(&s, &TerminalSession::failed);
        connect(&s, &TerminalSession::verifyHost, this, [](HostKey, DecisionPtr d) { d->resolve(1); });
        s.start();
        QTRY_VERIFY_WITH_TIMEOUT(!out.isEmpty() || !failure.isEmpty(), 15000);
        QVERIFY2(failure.isEmpty(), failure.isEmpty() ? "" : qPrintable(failure.first()[0].toString()));
        s.close();
        QVERIFY(s.wait(15000));
    }
    void recursiveTransfers() {
        SftpSession s(profile(), secret());
        QSignalSpy listed(&s, &SftpSession::listed), finished(&s, &SftpSession::taskFinished),
            failure(&s, &SftpSession::failed);
        connect(&s, &SftpSession::verifyHost, this, [](HostKey, DecisionPtr d) { d->resolve(1); });
        int conflicts = 0, conflictAnswer = 1;
        DecisionPtr pendingDecision;
        connect(&s, &SftpSession::conflict, this, [&](QString, DecisionPtr d) {
            ++conflicts;
            if (conflictAnswer < 0)
                pendingDecision = d;
            else
                d->resolve(conflictAnswer);
        });
        s.start();
        QTRY_VERIFY_WITH_TIMEOUT(!listed.isEmpty() || !failure.isEmpty(), 15000);
        QVERIFY(failure.isEmpty());
        QTemporaryDir source, target;
        QDir().mkpath(source.filePath("tree/sub"));
        QDir().mkpath(source.filePath("tree/.git"));
        auto write = [](QString path, QByteArray bytes) {
            QFile f(path);
            if (!f.open(QIODevice::WriteOnly))
                return false;
            return f.write(bytes) == bytes.size();
        };
        QVERIFY(write(source.filePath("tree/sub/中文.txt"), "payload"));
        QVERIFY(write(source.filePath("tree/.git/config"), "excluded"));
        const auto remote =
            remoteJoin(listed.first()[0].toString(),
                       "wiseshell-test-" + QUuid::createUuid().toString(QUuid::WithoutBraces));
        auto execute = [&](FileOperation op, QString from, QString to = QString()) {
            FileTask t;
            t.operation = op;
            t.source = from;
            t.destination = to;
            s.enqueue(t);
            return t.id;
        };
        auto result = [&](const QString &id) {
            for (const auto &row : finished)
                if (row[0].toString() == id)
                    return row;
            return QList<QVariant>{};
        };
        auto id = execute(FileOperation::Upload, source.filePath("tree"), remote);
        QTRY_VERIFY_WITH_TIMEOUT(!result(id).isEmpty(), 20000);
        QVERIFY2(result(id)[2].toBool(), qPrintable(result(id)[1].toString()));
        id = execute(FileOperation::Mkdir, remoteJoin(remote, ".git"));
        QTRY_VERIFY_WITH_TIMEOUT(!result(id).isEmpty(), 10000);
        QVERIFY(result(id)[2].toBool());
        id = execute(FileOperation::Upload, source.filePath("tree"), remote);
        QTRY_VERIFY_WITH_TIMEOUT(!result(id).isEmpty(), 20000);
        QVERIFY2(result(id)[2].toBool(), qPrintable(result(id)[1].toString()));
        QVERIFY(conflicts > 0);
        id = execute(FileOperation::List, remote);
        QTRY_VERIFY_WITH_TIMEOUT(!result(id).isEmpty(), 10000);
        bool kept = false;
        for (auto e : listed.last()[1].value<QList<RemoteEntry>>())
            if (e.name == ".git")
                kept = true;
        QVERIFY(kept);
        id = execute(FileOperation::Download, remote, target.filePath("copy"));
        QTRY_VERIFY_WITH_TIMEOUT(!result(id).isEmpty(), 20000);
        QVERIFY(result(id)[2].toBool());
        QFile received(target.filePath("copy/sub/中文.txt"));
        QVERIFY(received.open(QIODevice::ReadOnly));
        QCOMPARE(received.readAll(), QByteArray("payload"));
        received.close();
        // Skip an overwrite and confirm the original remote content survives.
        QVERIFY(write(source.filePath("tree/sub/中文.txt"), "replacement"));
        conflictAnswer = 2;
        id = execute(FileOperation::Upload, source.filePath("tree"), remote);
        QTRY_VERIFY_WITH_TIMEOUT(!result(id).isEmpty(), 10000);
        QVERIFY(result(id)[2].toBool());
        conflictAnswer = 1;
        id = execute(FileOperation::Download, remote, target.filePath("copy"));
        QTRY_VERIFY_WITH_TIMEOUT(!result(id).isEmpty(), 10000);
        QVERIFY(result(id)[2].toBool());
        QVERIFY(received.open(QIODevice::ReadOnly));
        QCOMPARE(received.readAll(), QByteArray("payload"));
        received.close();
        // Cancel both a queued task and a running task waiting for a conflict decision.
        conflictAnswer = -1;
        const auto waiting = execute(FileOperation::Upload, source.filePath("tree"), remote);
        QTRY_VERIFY_WITH_TIMEOUT(bool(pendingDecision), 10000);
        const auto queued = execute(FileOperation::List, remote);
        s.cancel(queued);
        QTRY_VERIFY(!result(queued).isEmpty());
        QVERIFY(!result(queued)[2].toBool());
        s.cancel(waiting);
        QTRY_VERIFY_WITH_TIMEOUT(!result(waiting).isEmpty(), 10000);
        QCOMPARE(result(waiting)[1].toString(), QStringLiteral("已取消"));
        pendingDecision->resolve(0);
        conflictAnswer = 1;
        // Cancel during an actual upload, before the staging file is renamed.
        QVERIFY(write(source.filePath("large.dat"), QByteArray(16 * 1024 * 1024, 'x')));
        const auto cancelConnection = connect(
            &s, &SftpSession::progress, &s,
            [&s](QString id, quint64 done, quint64, double) {
                if (done)
                    s.cancel(id);
            },
            Qt::DirectConnection);
        id = execute(FileOperation::Upload, source.filePath("large.dat"), remoteJoin(remote, "large.dat"));
        QTRY_VERIFY_WITH_TIMEOUT(!result(id).isEmpty(), 15000);
        disconnect(cancelConnection);
        QCOMPARE(result(id)[1].toString(), QStringLiteral("已取消"));
        id = execute(FileOperation::List, remote);
        QTRY_VERIFY_WITH_TIMEOUT(!result(id).isEmpty(), 10000);
        for (const auto &entry : listed.last()[1].value<QList<RemoteEntry>>()) {
            QVERIFY(entry.name != "large.dat");
            QVERIFY(!entry.name.contains(".wiseshell-"));
        }
        id = execute(FileOperation::Remove, remote);
        QTRY_VERIFY_WITH_TIMEOUT(!result(id).isEmpty(), 10000);
        QVERIFY(result(id)[2].toBool());
        s.close();
        QVERIFY(s.wait(15000));
    }
};
QTEST_GUILESS_MAIN(IntegrationTests)
#include "IntegrationTests.moc"
