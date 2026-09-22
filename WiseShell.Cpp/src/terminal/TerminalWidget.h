#pragma once
#include "core/Workspace.h"
#include <QAbstractScrollArea>
#include <QPoint>
#include <QTimer>
#include <deque>
#ifdef small
#pragma push_macro("small")
#undef small
#define WISESHELL_RESTORE_SMALL_MACRO
#endif
#include <vterm.h>
#ifdef WISESHELL_RESTORE_SMALL_MACRO
#pragma pop_macro("small")
#undef WISESHELL_RESTORE_SMALL_MACRO
#endif
namespace wise {
class TerminalWidget final : public QAbstractScrollArea {
    Q_OBJECT
  public:
    explicit TerminalWidget(QWidget *parent = nullptr);
    ~TerminalWidget() override;
    void feed(const QByteArray &bytes);
    void setSettings(const TerminalSettings &);
    QString screenText() const;
    QString selectedText() const;
    QSize terminalSize() const {
        return {columns_, rows_};
    }
    int historyLines() const {
        return int(history_.size());
    }
    void copy();
    void paste();
  signals:
    void input(QByteArray bytes);
    void terminalResized(int columns, int rows);
    void titleChanged(QString title);

  protected:
    bool event(QEvent *) override;
    void paintEvent(QPaintEvent *) override;
    void resizeEvent(QResizeEvent *) override;
    void keyPressEvent(QKeyEvent *) override;
    void inputMethodEvent(QInputMethodEvent *) override;
    QVariant inputMethodQuery(Qt::InputMethodQuery) const override;
    void mousePressEvent(QMouseEvent *) override;
    void mouseMoveEvent(QMouseEvent *) override;
    void mouseReleaseEvent(QMouseEvent *) override;
    void wheelEvent(QWheelEvent *) override;
    void contextMenuEvent(QContextMenuEvent *) override;

  private:
    static int damage(VTermRect, void *);
    static int cursor(VTermPos, VTermPos, int, void *);
    static int termProperty(VTermProp, VTermValue *, void *);
    static int pushLine(int, const VTermScreenCell *, void *);
    static int popLine(int, VTermScreenCell *, void *);
    static int bell(void *);
    static void output(const char *, size_t, void *);
    void dimensions();
    void refreshScroll();
    QPoint pointAt(const QPoint &) const;
    VTermScreenCell cellAt(int absoluteRow, int column) const;
    QColor color(VTermColor, bool foreground) const;
    bool selected(int row, int col) const;
    VTerm *term_ = nullptr;
    VTermScreen *screen_ = nullptr;
    VTermState *state_ = nullptr;
    TerminalSettings settings_;
    std::deque<QVector<VTermScreenCell>> history_;
    int rows_ = 24, columns_ = 80, cellWidth_ = 10, cellHeight_ = 20, ascent_ = 16;
    VTermPos cursor_{0, 0};
    bool cursorVisible_ = true, blink_ = true, dragging_ = false, alternate_ = false;
    QPoint anchor_{-1, -1}, selection_{-1, -1};
    QString preedit_;
    QByteArray titleBuffer_;
    QTimer blinkTimer_;
};
} // namespace wise
