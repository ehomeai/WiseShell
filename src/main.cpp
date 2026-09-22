#include "transport/SftpSession.h"
#include "ui/MainWindow.h"
#include <QApplication>
#include <QDir>
#include <QLockFile>
#include <QMessageBox>
#include <QStyleFactory>
#include <QTimer>
int main(int argc, char **argv) {
    QApplication app(argc, argv);
    app.setStyle(QStyleFactory::create(QStringLiteral("Fusion")));
    app.setStyleSheet(QStringLiteral(R"(
        QMainWindow { background: #f5f7fb; }
        QMenuBar { background: #ffffff; border-bottom: 1px solid #e6eaf0; padding: 2px 6px; }
        QMenuBar::item { padding: 6px 9px; border-radius: 5px; }
        QMenuBar::item:selected, QMenu::item:selected { background: #e8f0ff; color: #155eef; }
        QMenu { background: #ffffff; border: 1px solid #dfe5ef; padding: 5px; }
        QMenu::item { padding: 7px 28px 7px 12px; border-radius: 4px; }
        QToolBar#mainToolbar { background: #ffffff; border: none; border-bottom: 1px solid #e6eaf0; spacing: 4px; padding: 5px 9px; }
        QToolButton { border: 1px solid transparent; border-radius: 6px; color: #344054; padding: 6px 8px; }
        QToolButton:hover { background: #eef4ff; color: #155eef; }
        QToolButton:pressed { background: #dce9ff; }
        QLineEdit, QComboBox, QSpinBox, QDoubleSpinBox { background: #ffffff; border: 1px solid #d0d5dd; border-radius: 6px; min-height: 28px; padding: 0 8px; selection-background-color: #84adff; }
        QLineEdit:focus, QComboBox:focus, QSpinBox:focus, QDoubleSpinBox:focus { border: 1px solid #528bff; }
        QTreeWidget, QTableWidget, QListWidget, QTreeView { background: #ffffff; border: 1px solid #e4e7ec; border-radius: 8px; outline: none; }
        QTreeWidget::item { min-height: 29px; padding: 1px 5px; border-radius: 5px; }
        QTreeWidget::item:hover { background: transparent; }
        QTreeWidget::item:selected { background: transparent; color: #344054; }
        QTreeWidget::branch:selected { background: #ffffff; }
        QTreeWidget#sessionTree::item { min-height: 20px; padding: 0px 2px; border-radius: 0; }
        QTableWidget#sessionProperties { background: #ffffff; border: 1px solid #c7cdd6; border-radius: 0; gridline-color: #c7cdd6; outline: none; }
        QTableWidget#sessionProperties::item { color: #101828; padding: 0 6px; }
        QTableWidget#sessionProperties::item:selected { background: #2186eb; color: #ffffff; }
        QTabWidget::pane { background: #ffffff; border: 1px solid #e4e7ec; border-radius: 8px; top: -1px; }
        QTabBar::tab { background: #eef1f6; color: #667085; border: 1px solid transparent; border-bottom: none; border-radius: 7px 7px 0 0; min-width: 94px; padding: 8px 12px; margin-right: 3px; }
        QTabBar::tab:selected { background: #ffffff; color: #155eef; border-color: #e4e7ec; font-weight: 600; }
        QTabBar::close-button { subcontrol-position: right; }
        QTabBar::close-button:hover { background: #e4e7ec; border-radius: 5px; }
        QLabel#sideTitle { color: #101828; font-size: 16px; font-weight: 700; }
        QLabel#sectionLabel { color: #667085; font-size: 11px; font-weight: 700; padding-top: 8px; }
        QFrame#sidePanel { background: #f9fafb; border-right: 1px solid #e4e7ec; }
        QFrame#propertyCard { background: #ffffff; border: 1px solid #e4e7ec; border-radius: 8px; }
        QLabel#welcome { color: #667085; font-size: 15px; line-height: 1.6; }
        QLabel#welcomeTitle { color: #101828; font-size: 27px; font-weight: 700; }
        QStatusBar { background: #ffffff; border-top: 1px solid #e6eaf0; color: #667085; }
        QStatusBar QLabel { padding-left: 6px; }
        QToolBar#sftpToolbar { background: #ffffff; border: none; border-top: 1px solid #e4e7ec; spacing: 2px; padding: 3px 5px; }
        QToolBar#sftpToolbar QToolButton { padding: 4px 7px; }
        QLabel#sftpLocationLabel { color: #344054; font-weight: 600; min-width: 58px; }
        QLabel#sftpSummary { color: #667085; min-height: 18px; padding: 0 3px; }
        QLineEdit#localPathInput, QLineEdit#remotePathInput { border-radius: 3px; min-height: 25px; }
        QTableWidget#sftpLocalView, QTableWidget#sftpRemoteView { border-radius: 3px; }
        QTableWidget#sftpLocalView::item, QTableWidget#sftpRemoteView::item { padding: 1px 5px; }
        QTableWidget#transferQueue { border-radius: 0; border-left: none; border-right: none; border-bottom: none; border-top: 1px solid #dfe5ef; }
        QTableWidget#transferQueue::item { padding: 1px 6px; }
        QProgressBar#transferProgress { min-width: 108px; border: 1px solid #b8c3d3; border-radius: 2px; background: #ffffff; text-align: center; color: #344054; }
        QProgressBar#transferProgress::chunk { background: #2f80ed; }
        QHeaderView::section { background: #fafbfc; color: #475467; border: none; border-bottom: 1px solid #e4e7ec; border-right: 1px solid #eef1f4; padding: 5px 7px; font-weight: 600; }
        QPushButton { background: #ffffff; border: 1px solid #d0d5dd; border-radius: 6px; min-height: 28px; padding: 0 12px; }
        QPushButton:hover { background: #f2f6ff; border-color: #84adff; }
        QPushButton:default { background: #246bfd; border-color: #246bfd; color: white; }
        QScrollBar:vertical { background: transparent; width: 11px; margin: 4px 2px; }
        QScrollBar::handle:vertical { background: #c7d0df; border-radius: 4px; min-height: 30px; }
        QScrollBar::handle:vertical:hover { background: #98a7bd; }
    )"));
#ifdef Q_OS_WIN
    app.setFont(QFont(QStringLiteral("Microsoft YaHei UI"), 10));
#elif defined(Q_OS_MACOS)
    app.setFont(QFont(QStringLiteral("PingFang SC"), 12));
#endif
    QCoreApplication::setApplicationName("WiseShellCpp");
    QCoreApplication::setApplicationVersion("1.0.0");
    qRegisterMetaType<wise::HostKey>();
    qRegisterMetaType<wise::DecisionPtr>();
    qRegisterMetaType<QList<wise::RemoteEntry>>();
    QDir().mkpath(wise::dataDirectory());
    QLockFile lock(QDir(wise::dataDirectory()).filePath("app.lock"));
    if (!lock.tryLock(100)) {
        QMessageBox::warning(nullptr, QStringLiteral("WiseShell"),
                             QStringLiteral("已有实例使用此数据目录。"));
        return 1;
    }
    try {
        wise::MainWindow window;
        window.show();
        if (app.arguments().contains("--smoke-test"))
            QTimer::singleShot(800, &app, [&] {
                const int screenshot = app.arguments().indexOf("--screenshot");
                if (screenshot >= 0 && screenshot + 1 < app.arguments().size())
                    window.grab().save(app.arguments().at(screenshot + 1));
                app.quit();
            });
        return app.exec();
    } catch (const std::exception &e) {
        QMessageBox::critical(nullptr, QStringLiteral("启动失败"), QString::fromUtf8(e.what()));
        return 1;
    }
}
