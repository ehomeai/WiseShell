#pragma once
#include "Connection.h"
#include <QSize>
#include <QThread>
namespace wise {
class ITerminalSession {
  public:
    virtual ~ITerminalSession() = default;
    virtual void sendInput(const QByteArray &) = 0;
    virtual void resizeTerminal(int columns, int rows) = 0;
    virtual void close() = 0;
};
class TerminalSession final : public QThread, public ITerminalSession {
    Q_OBJECT
  public:
    TerminalSession(Profile profile, QString secret, QObject *parent = nullptr);
    ~TerminalSession() override;
    void sendInput(const QByteArray &) override;
    void resizeTerminal(int, int) override;
    void close() override;
  signals:
    void output(QByteArray bytes);
    void state(QString text);
    void failed(QString message, bool authentication);
    void verifyHost(wise::HostKey key, wise::DecisionPtr decision);

  protected:
    void run() override;

  private:
    Profile profile_;
    QString secret_;
    std::atomic_bool stop_{false};
    QMutex mutex_;
    QByteArray input_;
    QSize size_{80, 24};
};
} // namespace wise
