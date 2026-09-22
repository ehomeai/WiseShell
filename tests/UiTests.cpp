#include "terminal/TerminalWidget.h"
#include "ui/MainWindow.h"
#include "ui/SessionDialog.h"
#include "ui/SftpWindow.h"
#include <QApplication>
#include <QCheckBox>
#include <QContextMenuEvent>
#include <QDir>
#include <QFile>
#include <QLineEdit>
#include <QMenu>
#include <QPushButton>
#include <QTableWidget>
#include <QTemporaryDir>
#include <QtTest>
using namespace wise;
class UiTests : public QObject {
    Q_OBJECT
    void screenshot(QWidget &widget, const QString &name) {
        const auto directory = qEnvironmentVariable("WISESHELL_TEST_SCREENSHOTS");
        if (directory.isEmpty())
            return;
        QDir().mkpath(directory);
        QVERIFY(widget.grab().save(QDir(directory).filePath(name + ".png")));
    }
  private slots:
    void initTestCase() {
#ifdef Q_OS_WIN
        QApplication::setFont(QFont(QStringLiteral("Microsoft YaHei UI"), 10));
#endif
    }
    void sessionTreePersistsRenameAndMove() {
        QTemporaryDir directory;
        SessionRepository repository(directory.path());
        Workspace workspace;
        Profile first, second;
        first.name = QStringLiteral("开发服务器");
        first.host = "dev.example.test";
        first.username = "developer";
        first.group = QStringLiteral("开发环境");
        first.favorite = true;
        second.name = QStringLiteral("测试服务器");
        second.host = "test.example.test";
        second.group = first.group;
        workspace.sessions = {first, second};
        workspace.folders = {first.group, QStringLiteral("归档")};
        repository.save(workspace);
        MainWindow window(nullptr, directory.path());
        window.show();
        QTest::qWait(30);
        auto *tree = window.findChild<SessionTree *>();
        QVERIFY(tree);
        QVERIFY(!tree->editTriggers().testFlag(QAbstractItemView::DoubleClicked));
        auto find = [&](const QString &id) -> QTreeWidgetItem * {
            for (QTreeWidgetItemIterator it(tree); *it; ++it)
                if ((*it)->data(0, Qt::UserRole).toString() == id)
                    return *it;
            return nullptr;
        };
        auto *item = find(first.id);
        QVERIFY(item);
        tree->setCurrentItem(item);
        auto *properties = window.findChild<QTableWidget *>(QStringLiteral("sessionProperties"));
        QVERIFY(properties);
        QCOMPARE(properties->rowCount(), 6);
        QCOMPARE(properties->item(0, 0)->text(), QStringLiteral("名称"));
        QCOMPARE(properties->item(0, 1)->text(), first.name);
        QCOMPARE(properties->item(3, 1)->text(), QStringLiteral("SSH"));
        item->setText(0, QStringLiteral("开发服务器（已重命名）"));
        QTRY_COMPARE(repository.load().sessions.first().name, QStringLiteral("开发服务器（已重命名）"));
        QTest::qWait(10);
        tree->moved(first.id, first.group, QStringLiteral("归档"));
        QCOMPARE(repository.load().sessions.first().group, QStringLiteral("归档"));
        auto *favorites = window.findChild<QCheckBox *>();
        QVERIFY(favorites);
        favorites->setChecked(true);
        QVERIFY(find(first.id));
        QVERIFY(!find(second.id));
        favorites->setChecked(false);
        auto *search = window.findChild<QLineEdit *>();
        QVERIFY(search);
        search->setText("test.example.test");
        QVERIFY(!find(first.id));
        QVERIFY(find(second.id));
        search->clear();
        tree->setCurrentItem(find(first.id));
        screenshot(window, "sessions");
    }
    void settingsAndRemotePickerRequest() {
        Profile profile;
        profile.name = QStringLiteral("服务器设置");
        profile.host = "example.test";
        profile.username = "user";
        profile.keyAuth = true;
        profile.privateKey = QStringLiteral("/keys/服务器.pem");
        SessionDialog dialog(profile, {QStringLiteral("开发环境")});
        dialog.show();
        QTest::qWait(20);
        QCOMPARE(dialog.value().host, profile.host);
        QVERIFY(dialog.value().keyAuth);
        QVERIFY(!dialog.secretChanged());
        dialog.setRemoteDirectory("/srv/app");
        QCOMPARE(dialog.value().remoteDirectory, QString("/srv/app"));
        QSignalSpy browse(&dialog, &SessionDialog::browseRemote);
        for (auto *button : dialog.findChildren<QPushButton *>())
            if (button->text() == QStringLiteral("浏览"))
                QTest::mouseClick(button, Qt::LeftButton);
        QCOMPARE(browse.size(), 1);
        screenshot(dialog, "settings");
    }
    void nativeTerminalPreviewAndSelection() {
        TerminalWidget terminal;
        TerminalSettings settings;
#ifdef Q_OS_WIN
        settings.fontFamily = "Consolas";
#endif
        settings.fontSize = 14;
        terminal.setSettings(settings);
        terminal.resize(980, 500);
        terminal.show();
        QTest::qWait(20);
        terminal.feed("\033[32muser@server\033[0m:~$ ls --color\r\n\033[34mprojects\033[0m  readme.txt  "
                      "\033[32mdeploy.sh\033[0m\r\n");
        terminal.feed(QStringLiteral("原生 C++ 终端 · UTF-8 中文\r\n").toUtf8());
        terminal.feed("\r\n\033[32muser@server\033[0m:~$ ");
        QTest::mousePress(terminal.viewport(), Qt::LeftButton, Qt::NoModifier, QPoint(2, 2));
        QTest::mouseMove(terminal.viewport(), QPoint(150, 2));
        QTest::mouseRelease(terminal.viewport(), Qt::LeftButton, Qt::NoModifier, QPoint(150, 2));
        QVERIFY(!terminal.selectedText().isEmpty());
        terminal.copy();
        screenshot(terminal, "terminal");
    }
    void terminalFailurePreview() {
        TerminalWidget terminal;
        terminal.resize(980, 420);
        terminal.show();
        QTest::qWait(20);
        terminal.feed(QStringLiteral("\033[2J\033[H\033[1;31m连接失败\033[0m\r\n\r\n"
                                     "\033[90m目标\033[0m  kfb@10.64.0.151:22\r\n"
                                     "\033[90m原因\033[0m  TCP 连接超时\r\n"
                                     "\033[90m建议\033[0m  确认 VPN / 内网已接入，并检查 SSH 服务、防火墙和端口。\r\n\r\n"
                                     "\033[90m按 Enter 立即重连，或关闭此标签页。\033[0m\r\n"
                                     "\033[90m详细诊断已在错误对话框中提供。\033[0m\r\n")
                          .toUtf8());
        QVERIFY(terminal.screenText().contains(QStringLiteral("TCP 连接超时")));
        QVERIFY(terminal.screenText().contains(QStringLiteral("按 Enter 立即重连")));
        screenshot(terminal, "terminal-failure");
    }
    void sftpAddressBarNavigatesLocalDirectoryOnEnter() {
        QTemporaryDir directory;
        QVERIFY(directory.isValid());
        const QString child = QDir(directory.path()).filePath(QStringLiteral("资料"));
        QVERIFY(QDir().mkpath(child));
        QFile file(QDir(child).filePath(QStringLiteral("readme.txt")));
        QVERIFY(file.open(QIODevice::WriteOnly));
        file.write("WiseShell");
        file.close();
        Profile profile;
        profile.localDirectory = directory.path();
        SftpWindow window(profile, {});
        window.show();
        QTest::qWait(20);
        auto *path = window.findChild<QLineEdit *>(QStringLiteral("localPathInput"));
        auto *table = window.findChild<QTableWidget *>(QStringLiteral("sftpLocalView"));
        QVERIFY(path);
        QVERIFY(table);
        QCOMPARE(table->item(0, 0)->text(), QStringLiteral(".."));
        path->setText(child);
        QTest::keyClick(path, Qt::Key_Return);
        QCOMPARE(path->text(), QDir::toNativeSeparators(QDir(child).absolutePath()));
        QCOMPARE(table->item(0, 0)->text(), QStringLiteral(".."));
        QVERIFY(table->rowCount() >= 2);
        screenshot(window, "sftp-address-bar");
    }
    void sftpBrowsersAcceptCustomContextMenus() {
        QTemporaryDir directory;
        QVERIFY(directory.isValid());
        QFile file(QDir(directory.path()).filePath(QStringLiteral("upload.txt")));
        QVERIFY(file.open(QIODevice::WriteOnly));
        file.write("WiseShell");
        file.close();

        Profile profile;
        profile.localDirectory = directory.path();
        SftpWindow window(profile, {});
        window.resize(900, 620);
        window.show();
        QTest::qWait(20);
        auto *view = window.findChild<QTableWidget *>(QStringLiteral("sftpLocalView"));
        QVERIFY(view);
        QVERIFY(view->rowCount() >= 2);

        auto *remote = window.findChild<QTableWidget *>(QStringLiteral("sftpRemoteView"));
        QVERIFY(remote);

        const auto viewportPosition = view->visualItemRect(view->item(1, 0)).center();
        const auto viewPosition = view->viewport()->mapTo(view, viewportPosition);
        QContextMenuEvent event(QContextMenuEvent::Mouse, viewPosition, view->mapToGlobal(viewPosition));
        QCoreApplication::sendEvent(view, &event);

        const auto menus = window.findChildren<QMenu *>();
        QVERIFY(!menus.isEmpty());
        auto *menu = menus.last();
        QStringList actions;
        for (const auto *action : menu->actions())
            actions.append(action->text());
        QCOMPARE(view->selectionModel()->selectedRows().size(), 1);
        QCOMPARE(view->item(view->currentRow(), 0)->text(), QStringLiteral("upload.txt"));
        QVERIFY(actions.contains(QStringLiteral("上传到远程 →")));
        QVERIFY(actions.contains(QStringLiteral("本地新建目录")));
        QVERIFY(actions.contains(QStringLiteral("本地重命名")));
        QVERIFY(actions.contains(QStringLiteral("本地删除")));
        QVERIFY(actions.contains(QStringLiteral("刷新")));
        menu->close();
        QTest::qWait(10);
    }
};
QTEST_MAIN(UiTests)
#include "UiTests.moc"
