#pragma once
#include "transport/SftpSession.h"
#include <QDialog>
class QLineEdit;
class QListWidget;
class QLabel;
namespace wise {
class RemoteDirectoryDialog final : public QDialog {
    Q_OBJECT
  public:
    RemoteDirectoryDialog(Profile profile, QString secret, QWidget *parent = nullptr);
    ~RemoteDirectoryDialog() override;
    QString directory() const;
    SftpSession *session() const {
        return session_;
    }

  private:
    void navigate(const QString &);
    SftpSession *session_;
    QLineEdit *path_;
    QListWidget *list_;
    QLabel *status_;
    QString current_;
};
} // namespace wise
