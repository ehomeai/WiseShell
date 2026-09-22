#pragma once
#include "core/Workspace.h"
#include <QDialog>
class QLineEdit;
class QSpinBox;
class QCheckBox;
class QComboBox;
class QDoubleSpinBox;
class QFontComboBox;
namespace wise {
class TerminalSettingsEditor final : public QWidget {
  public:
    TerminalSettingsEditor(const TerminalSettings &, QWidget *parent = nullptr);
    TerminalSettings value() const;

  private:
    QComboBox *theme_;
    QFontComboBox *font_;
    QDoubleSpinBox *size_;
    QSpinBox *scrollback_;
};
class SessionDialog final : public QDialog {
    Q_OBJECT
  public:
    SessionDialog(const Profile &, const QStringList &groups, QWidget *parent = nullptr);
    Profile value() const;
    QString secret() const;
    bool secretChanged() const;
    void setRemoteDirectory(const QString &path);
  signals:
    void browseRemote(wise::Profile profile);

  private:
    Profile original_;
    QLineEdit *name_, *host_, *user_, *key_, *secret_, *local_, *remote_;
    QComboBox *group_, *auth_;
    QSpinBox *port_, *keepAlive_;
    QCheckBox *remember_, *favorite_;
    TerminalSettingsEditor *terminal_;
};
} // namespace wise
