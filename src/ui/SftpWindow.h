#pragma once
#include "transport/SftpSession.h"
#include <QHash>
#include <QMainWindow>
class QTableWidget;
class QLineEdit;
class QLabel;
class QPoint;
namespace wise {
class SftpWindow final : public QMainWindow {
    Q_OBJECT
  public:
    SftpWindow(Profile, QString secret, QWidget *parent = nullptr);
    ~SftpWindow() override;
    SftpSession *session() const {
        return session_;
    }
  signals:
    void defaultDirectories(QString id, QString local, QString remote);

  protected:
    void closeEvent(QCloseEvent *) override;
    bool eventFilter(QObject *watched, QEvent *event) override;

  private:
    void localNavigate(QString);
    void remoteNavigate(QString);
    void populateLocal(const QString &);
    void transfer(bool upload);
    void fileAction(bool remote, FileOperation);
    void showBrowserMenu(bool remote, const QPoint &globalPosition);
    void cancelSelectedTransfers();
    void queue(FileTask);
    void refresh();
    void trimHistory();
    int taskRow(const QString &) const;
    Profile profile_;
    SftpSession *session_;
    QTableWidget *localView_, *remoteView_, *transfers_;
    QLineEdit *localPath_, *remotePath_;
    QLabel *state_, *localSummary_, *remoteSummary_;
    QString remoteDirectory_;
    QHash<QString, FileTask> active_;
    bool closing_ = false;
};
} // namespace wise
