#pragma once
#include "core/Credentials.h"
#include "core/Workspace.h"
#include "transport/TerminalSession.h"
#include <QHash>
#include <QMainWindow>
#include <QTreeWidget>
class QTabWidget;
class QLineEdit;
class QLabel;
class QCheckBox;
class QTableWidget;
namespace wise {
class SessionTree final : public QTreeWidget {
    Q_OBJECT
  public:
    explicit SessionTree(QWidget *parent = nullptr);
  signals:
    void moved(QString id, QString oldGroup, QString targetGroup);

  protected:
    void drawBranches(QPainter *, const QRect &, const QModelIndex &) const override;
    void dropEvent(QDropEvent *) override;
};
class MainWindow final : public QMainWindow {
    Q_OBJECT
  public:
    explicit MainWindow(QWidget *parent = nullptr, QString directory = dataDirectory());
    ~MainWindow() override;

  private:
    Profile *selectedProfile();
    QString selectedGroup() const;
    void rebuild();
    bool save();
    void editSession(bool create);
    void removeSelected();
    void addGroup();
    void connectSelected(bool sftp);
    void withSecret(Profile, std::function<void(QString)>);
    void rememberSuccessfulSecret(const Profile &, QString);
    void openTerminal(Profile, QString);
    void openSftp(Profile, QString);
    void verify(const HostKey &, DecisionPtr);
    void closeTab(int);
    void defaults();
    void reconnectCurrent();
    void currentSftp();
    void showError(const QString &);
    void updateProperties(const Profile *);
    Workspace workspace_;
    SessionRepository repository_;
    HostKeyTrustStore hosts_;
    Credentials credentials_;
    SessionTree *tree_;
    QTabWidget *tabs_;
    QLineEdit *search_;
    QCheckBox *favorites_;
    QLabel *status_;
    QTableWidget *properties_;
    QHash<QWidget *, TerminalSession *> terminals_;
    bool rebuilding_ = false;
};
} // namespace wise
