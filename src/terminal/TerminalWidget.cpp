#include "TerminalWidget.h"
#include <QApplication>
#include <QClipboard>
#include <QInputMethodEvent>
#include <QKeyEvent>
#include <QMenu>
#include <QMouseEvent>
#include <QPainter>
#include <QScrollBar>
#include <algorithm>
#include <cstring>
namespace wise {
static QString cellString(const VTermScreenCell &c) {
    QString result;
    for (auto value : c.chars) {
        if (value == 0)
            break;
        if (value == 0xffffffffU)
            continue;
        char32_t ch = value;
        result += QString::fromUcs4(&ch, 1);
    }
    return result.isEmpty() ? QString(" ") : result;
}
TerminalWidget::TerminalWidget(QWidget *parent) : QAbstractScrollArea(parent) {
    setFocusPolicy(Qt::StrongFocus);
    setAttribute(Qt::WA_InputMethodEnabled);
    viewport()->setAttribute(Qt::WA_InputMethodEnabled);
    setHorizontalScrollBarPolicy(Qt::ScrollBarAlwaysOff);
    setVerticalScrollBarPolicy(Qt::ScrollBarAlwaysOn);
    term_ = vterm_new(rows_, columns_);
    vterm_set_utf8(term_, 1);
    screen_ = vterm_obtain_screen(term_);
    state_ = vterm_obtain_state(term_);
    static const VTermScreenCallbacks callbacks = [] {
        VTermScreenCallbacks c{};
        c.damage = damage;
        c.movecursor = cursor;
        c.settermprop = termProperty;
        c.bell = bell;
        c.sb_pushline = pushLine;
        c.sb_popline = popLine;
        return c;
    }();
    vterm_screen_set_callbacks(screen_, &callbacks, this);
    vterm_output_set_callback(term_, output, this);
    vterm_screen_enable_altscreen(screen_, 1);
    vterm_screen_set_damage_merge(screen_, VTERM_DAMAGE_SCROLL);
    vterm_screen_reset(screen_, 1);
    connect(verticalScrollBar(), &QScrollBar::valueChanged, viewport(), QOverload<>::of(&QWidget::update));
    connect(&blinkTimer_, &QTimer::timeout, this, [this] {
        blink_ = !blink_;
        viewport()->update();
    });
    blinkTimer_.start(500);
    setSettings(settings_);
}
TerminalWidget::~TerminalWidget() {
    vterm_free(term_);
}
bool TerminalWidget::event(QEvent *event) {
    if (event->type() == QEvent::ShortcutOverride) {
        const auto *key = static_cast<QKeyEvent *>(event);
        if (key->key() == Qt::Key_F2 && key->modifiers() == Qt::NoModifier) {
            event->ignore();
            return false;
        }
        event->accept();
        return true;
    }
    if (event->type() == QEvent::KeyPress) {
        auto *key = static_cast<QKeyEvent *>(event);
        if (key->key() == Qt::Key_Tab || key->key() == Qt::Key_Backtab) {
            keyPressEvent(key);
            return true;
        }
    }
    return QAbstractScrollArea::event(event);
}
int TerminalWidget::damage(VTermRect, void *u) {
    static_cast<TerminalWidget *>(u)->viewport()->update();
    return 1;
}
int TerminalWidget::cursor(VTermPos p, VTermPos, int visible, void *u) {
    auto *w = static_cast<TerminalWidget *>(u);
    w->cursor_ = p;
    w->cursorVisible_ = visible;
    w->viewport()->update();
    return 1;
}
int TerminalWidget::termProperty(VTermProp prop, VTermValue *value, void *u) {
    auto *w = static_cast<TerminalWidget *>(u);
    if (prop == VTERM_PROP_TITLE) {
        const auto &s = value->string;
        if (s.initial)
            w->titleBuffer_.clear();
        if (w->titleBuffer_.size() < 8192)
            w->titleBuffer_.append(s.str, qsizetype(s.len));
        if (s.final)
            emit w->titleChanged(QString::fromUtf8(w->titleBuffer_));
    }
    if (prop == VTERM_PROP_CURSORVISIBLE)
        w->cursorVisible_ = value->boolean;
    if (prop == VTERM_PROP_ALTSCREEN) {
        w->alternate_ = value->boolean;
        w->anchor_ = {-1, -1};
        w->selection_ = {-1, -1};
        w->refreshScroll();
    }
    return 1;
}
int TerminalWidget::bell(void *) {
    QApplication::beep();
    return 1;
}
int TerminalWidget::pushLine(int cols, const VTermScreenCell *cells, void *u) {
    auto *w = static_cast<TerminalWidget *>(u);
    if (w->alternate_ || w->settings_.scrollback == 0)
        return 1;
    w->history_.emplace_back(cells, cells + cols);
    if (int(w->history_.size()) > w->settings_.scrollback) {
        w->history_.pop_front();
        w->anchor_.ry() -= 1;
        w->selection_.ry() -= 1;
    }
    return 1;
}
int TerminalWidget::popLine(int cols, VTermScreenCell *cells, void *u) {
    auto *w = static_cast<TerminalWidget *>(u);
    if (w->history_.empty() || w->alternate_)
        return 0;
    auto line = w->history_.back();
    w->history_.pop_back();
    std::memset(cells, 0, sizeof(*cells) * cols);
    for (int c = 0; c < cols; ++c) {
        if (c < line.size())
            cells[c] = line[c];
        else {
            cells[c].width = 1;
            vterm_state_get_default_colors(w->state_, &cells[c].fg, &cells[c].bg);
        }
    }
    return 1;
}
void TerminalWidget::output(const char *s, size_t length, void *u) {
    emit static_cast<TerminalWidget *>(u)->input(QByteArray(s, qsizetype(length)));
}
void TerminalWidget::feed(const QByteArray &bytes) {
    const bool bottom = verticalScrollBar()->value() == verticalScrollBar()->maximum();
    vterm_input_write(term_, bytes.constData(), size_t(bytes.size()));
    vterm_screen_flush_damage(screen_);
    refreshScroll();
    if (bottom)
        verticalScrollBar()->setValue(verticalScrollBar()->maximum());
    viewport()->update();
}
void TerminalWidget::refreshScroll() {
    verticalScrollBar()->setRange(0, alternate_ ? 0 : int(history_.size()));
    verticalScrollBar()->setPageStep(rows_);
}
void TerminalWidget::setSettings(const TerminalSettings &s) {
    settings_ = s;
    // "monospace" is a generic family name.  On Windows it can resolve to a
    // proportional fallback, which gives each terminal cell a visible gap.
    QString family = s.fontFamily.trimmed();
    if (family.isEmpty() || family.compare("monospace", Qt::CaseInsensitive) == 0) {
#ifdef Q_OS_WIN
        family = QStringLiteral("DejaVu Sans Mono");
#else
        family = QStringLiteral("monospace");
#endif
    }
    QFont f(family);
    f.setStyleHint(QFont::Monospace);
    f.setFixedPitch(true);
    // Earlier previews used 18 pt with the generic family.  Render that
    // legacy default at a practical terminal size while preserving explicit
    // font choices in the session settings dialog.
    const bool legacyDefault = s.fontFamily.compare("monospace", Qt::CaseInsensitive) == 0 && s.fontSize == 18;
    f.setPointSizeF(legacyDefault ? 16.0 : s.fontSize);
    setFont(f);
    QFontMetrics metrics(f);
    cellWidth_ = std::max(1, metrics.horizontalAdvance('M'));
    cellHeight_ = metrics.height();
    ascent_ = metrics.ascent();
    while (int(history_.size()) > s.scrollback)
        history_.pop_front();
    const QColor fg = s.theme == "Light"       ? QColor("#202530")
                      : s.theme == "Solarized" ? QColor("#839496")
                                               : QColor("#e2e8f0");
    const QColor bg = s.theme == "Light"       ? QColor("#fafafa")
                      : s.theme == "Solarized" ? QColor("#002b36")
                                               : QColor("#0b1220");
    VTermColor vfg, vbg;
    vterm_color_rgb(&vfg, uint8_t(fg.red()), uint8_t(fg.green()), uint8_t(fg.blue()));
    vterm_color_rgb(&vbg, uint8_t(bg.red()), uint8_t(bg.green()), uint8_t(bg.blue()));
    vterm_state_set_default_colors(state_, &vfg, &vbg);
    const QStringList darkPalette = {"#111827", "#ef4444", "#22c55e", "#eab308", "#60a5fa", "#c084fc",
                                     "#22d3ee", "#e5e7eb", "#6b7280", "#f87171", "#4ade80", "#fde047",
                                     "#93c5fd", "#d8b4fe", "#67e8f9", "#ffffff"};
    const QStringList lightPalette = {"#111827", "#b91c1c", "#15803d", "#a16207", "#1d4ed8", "#7e22ce",
                                      "#0e7490", "#374151", "#6b7280", "#dc2626", "#16a34a", "#ca8a04",
                                      "#2563eb", "#9333ea", "#0891b2", "#4b5563"};
    for (int i = 0; i < 16; ++i) {
        const QColor c((s.theme == "Light" ? lightPalette : darkPalette)[i]);
        VTermColor paletteColor;
        vterm_color_rgb(&paletteColor, uint8_t(c.red()), uint8_t(c.green()), uint8_t(c.blue()));
        vterm_state_set_palette_color(state_, i, &paletteColor);
    }
    dimensions();
    refreshScroll();
    viewport()->update();
}
QColor TerminalWidget::color(VTermColor value, bool foreground) const {
    if ((foreground && VTERM_COLOR_IS_DEFAULT_FG(&value)) ||
        (!foreground && VTERM_COLOR_IS_DEFAULT_BG(&value))) {
        VTermColor f, b;
        vterm_state_get_default_colors(state_, &f, &b);
        value = foreground ? f : b;
    }
    vterm_screen_convert_color_to_rgb(screen_, &value);
    return {value.rgb.red, value.rgb.green, value.rgb.blue};
}
void TerminalWidget::dimensions() {
    int cols = std::max(2, viewport()->width() / cellWidth_),
        rows = std::max(1, viewport()->height() / cellHeight_);
    if (cols == columns_ && rows == rows_)
        return;
    columns_ = cols;
    rows_ = rows;
    vterm_set_size(term_, rows_, columns_);
    vterm_screen_flush_damage(screen_);
    refreshScroll();
    emit terminalResized(cols, rows);
}
void TerminalWidget::resizeEvent(QResizeEvent *e) {
    QAbstractScrollArea::resizeEvent(e);
    dimensions();
}
VTermScreenCell TerminalWidget::cellAt(int row, int col) const {
    VTermScreenCell c{};
    c.width = 1;
    vterm_state_get_default_colors(state_, &c.fg, &c.bg);
    if (!alternate_ && row < int(history_.size())) {
        if (row >= 0 && col >= 0 && col < history_[size_t(row)].size())
            return history_[size_t(row)][col];
        return c;
    }
    row -= alternate_ ? 0 : int(history_.size());
    if (row >= 0 && row < rows_ && col >= 0 && col < columns_)
        vterm_screen_get_cell(screen_, {row, col}, &c);
    return c;
}
bool TerminalWidget::selected(int row, int col) const {
    if (anchor_.y() < 0 || selection_.y() < 0)
        return false;
    auto a = anchor_, b = selection_;
    if (a.y() > b.y() || (a.y() == b.y() && a.x() > b.x()))
        std::swap(a, b);
    return (row > a.y() || (row == a.y() && col >= a.x())) && (row < b.y() || (row == b.y() && col <= b.x()));
}
void TerminalWidget::paintEvent(QPaintEvent *) {
    QPainter p(viewport());
    p.setFont(font());
    VTermColor fg, bg;
    vterm_state_get_default_colors(state_, &fg, &bg);
    p.fillRect(viewport()->rect(), color(bg, false));
    int offset = verticalScrollBar()->value();
    for (int r = 0; r < rows_; ++r)
        for (int c = 0; c < columns_; ++c) {
            auto cell = cellAt(offset + r, c);
            if (cell.chars[0] == 0xffffffffU)
                continue;
            auto foreground = color(cell.fg, true), background = color(cell.bg, false);
            if (cell.attrs.reverse)
                std::swap(foreground, background);
            if (selected(offset + r, c)) {
                background = QColor("#285a91");
                foreground = Qt::white;
            }
            QRect rect(c * cellWidth_, r * cellHeight_, cellWidth_ * std::max(1, int(cell.width)),
                       cellHeight_);
            p.fillRect(rect, background);
            QFont f = font();
            f.setBold(cell.attrs.bold);
            f.setItalic(cell.attrs.italic);
            f.setUnderline(cell.attrs.underline);
            f.setStrikeOut(cell.attrs.strike);
            p.setFont(f);
            p.setPen(foreground);
            if (!cell.attrs.conceal && (!cell.attrs.blink || blink_))
                p.drawText(rect.x(), rect.y() + ascent_, cellString(cell));
        }
    int cr = cursor_.row + (alternate_ ? 0 : int(history_.size())) - offset;
    if (cursorVisible_ && hasFocus() && cr >= 0 && cr < rows_) {
        const QRect cursorRect(cursor_.col * cellWidth_, cr * cellHeight_, cellWidth_, cellHeight_);
        const QColor cursorColor = settings_.theme == "Light" ? QColor("#16a34a") : QColor("#20e36b");
        p.fillRect(cursorRect, cursorColor);
    }
    if (!preedit_.isEmpty()) {
        p.setPen(color(fg, true));
        p.drawText(cursor_.col * cellWidth_, cr * cellHeight_ + ascent_, preedit_);
    }
}
QString TerminalWidget::screenText() const {
    QString result;
    for (int r = 0; r < rows_; ++r) {
        QString line;
        for (int c = 0; c < columns_; ++c) {
            auto cell = cellAt((alternate_ ? 0 : int(history_.size())) + r, c);
            if (cell.chars[0] != 0xffffffffU)
                line += cellString(cell);
        }
        result += line.trimmed() + "\n";
    }
    return result;
}
QPoint TerminalWidget::pointAt(const QPoint &p) const {
    return {std::clamp(p.x() / cellWidth_, 0, columns_ - 1),
            verticalScrollBar()->value() + std::clamp(p.y() / cellHeight_, 0, rows_ - 1)};
}
void TerminalWidget::mousePressEvent(QMouseEvent *e) {
    if (e->button() == Qt::LeftButton) {
        setFocus();
        anchor_ = selection_ = pointAt(e->pos());
        dragging_ = true;
        viewport()->update();
    }
}
void TerminalWidget::mouseMoveEvent(QMouseEvent *e) {
    if (dragging_) {
        if (e->pos().y() < 0)
            verticalScrollBar()->setValue(verticalScrollBar()->value() - 1);
        else if (e->pos().y() >= viewport()->height())
            verticalScrollBar()->setValue(verticalScrollBar()->value() + 1);
        selection_ = pointAt(e->pos());
        viewport()->update();
    }
}
void TerminalWidget::mouseReleaseEvent(QMouseEvent *e) {
    if (e->button() == Qt::LeftButton)
        dragging_ = false;
}
QString TerminalWidget::selectedText() const {
    if (anchor_.y() < 0 || selection_.y() < 0)
        return {};
    auto a = anchor_, b = selection_;
    if (a.y() > b.y() || (a.y() == b.y() && a.x() > b.x()))
        std::swap(a, b);
    QString text;
    for (int r = a.y(); r <= b.y(); ++r) {
        QString line;
        for (int c = (r == a.y() ? a.x() : 0); c <= (r == b.y() ? b.x() : columns_ - 1); ++c) {
            auto cell = cellAt(r, c);
            if (cell.chars[0] != 0xffffffffU)
                line += cellString(cell);
        }
        while (line.endsWith(' '))
            line.chop(1);
        text += line;
        if (r < b.y())
            text += '\n';
    }
    return text;
}
void TerminalWidget::copy() {
    if (!selectedText().isEmpty())
        QApplication::clipboard()->setText(selectedText());
}
void TerminalWidget::paste() {
    auto text = QApplication::clipboard()->text();
    text.replace("\r\n", "\n");
    text.replace('\n', '\r');
    if (text.size() > 1024 * 1024) {
        QApplication::beep();
        return;
    }
    vterm_keyboard_start_paste(term_);
    auto bytes = text.toUtf8();
    emit input(bytes);
    vterm_keyboard_end_paste(term_);
}
void TerminalWidget::keyPressEvent(QKeyEvent *e) {
    const bool ctrl = e->modifiers().testFlag(Qt::ControlModifier),
               shift = e->modifiers().testFlag(Qt::ShiftModifier);
    if (e->key() == Qt::Key_Insert && e->modifiers() == Qt::ControlModifier) {
        copy();
        return;
    }
    if (e->key() == Qt::Key_Insert && e->modifiers() == Qt::ShiftModifier) {
        paste();
        return;
    }
    if (shift && (e->key() == Qt::Key_PageUp || e->key() == Qt::Key_PageDown)) {
        verticalScrollBar()->setValue(verticalScrollBar()->value() +
                                      (e->key() == Qt::Key_PageUp ? -rows_ : rows_));
        return;
    }
    verticalScrollBar()->setValue(verticalScrollBar()->maximum());
    anchor_ = selection_ = {-1, -1};
    int mods = 0;
    if (ctrl)
        mods |= VTERM_MOD_CTRL;
    if (shift)
        mods |= VTERM_MOD_SHIFT;
    if (e->modifiers().testFlag(Qt::AltModifier))
        mods |= VTERM_MOD_ALT;
    VTermKey key = VTERM_KEY_NONE;
    switch (e->key()) {
    case Qt::Key_Return:
    case Qt::Key_Enter:
        key = VTERM_KEY_ENTER;
        break;
    case Qt::Key_Tab:
    case Qt::Key_Backtab:
        key = VTERM_KEY_TAB;
        break;
    case Qt::Key_Backspace:
        key = VTERM_KEY_BACKSPACE;
        break;
    case Qt::Key_Escape:
        key = VTERM_KEY_ESCAPE;
        break;
    case Qt::Key_Up:
        key = VTERM_KEY_UP;
        break;
    case Qt::Key_Down:
        key = VTERM_KEY_DOWN;
        break;
    case Qt::Key_Left:
        key = VTERM_KEY_LEFT;
        break;
    case Qt::Key_Right:
        key = VTERM_KEY_RIGHT;
        break;
    case Qt::Key_Insert:
        key = VTERM_KEY_INS;
        break;
    case Qt::Key_Delete:
        key = VTERM_KEY_DEL;
        break;
    case Qt::Key_Home:
        key = VTERM_KEY_HOME;
        break;
    case Qt::Key_End:
        key = VTERM_KEY_END;
        break;
    case Qt::Key_PageUp:
        key = VTERM_KEY_PAGEUP;
        break;
    case Qt::Key_PageDown:
        key = VTERM_KEY_PAGEDOWN;
        break;
    default:
        if (e->key() >= Qt::Key_F1 && e->key() <= Qt::Key_F35)
            key = VTermKey(VTERM_KEY_FUNCTION(e->key() - Qt::Key_F1 + 1));
    }
    if (key != VTERM_KEY_NONE)
        vterm_keyboard_key(term_, key, VTermModifier(mods));
    else if (ctrl && e->key() >= Qt::Key_A && e->key() <= Qt::Key_Z)
        vterm_keyboard_unichar(term_, uint32_t('a' + e->key() - Qt::Key_A), VTermModifier(mods));
    else
        for (auto ch : e->text().toUcs4())
            vterm_keyboard_unichar(term_, ch, VTermModifier(mods & ~VTERM_MOD_SHIFT));
    viewport()->update();
}
void TerminalWidget::inputMethodEvent(QInputMethodEvent *e) {
    preedit_ = e->preeditString();
    for (auto ch : e->commitString().toUcs4())
        vterm_keyboard_unichar(term_, ch, VTERM_MOD_NONE);
    e->accept();
    viewport()->update();
}
QVariant TerminalWidget::inputMethodQuery(Qt::InputMethodQuery q) const {
    if (q == Qt::ImCursorRectangle)
        return QRect(cursor_.col * cellWidth_, cursor_.row * cellHeight_, cellWidth_, cellHeight_);
    if (q == Qt::ImFont)
        return font();
    return QAbstractScrollArea::inputMethodQuery(q);
}
void TerminalWidget::wheelEvent(QWheelEvent *e) {
    verticalScrollBar()->setValue(verticalScrollBar()->value() - e->angleDelta().y() / 40);
    e->accept();
}
void TerminalWidget::contextMenuEvent(QContextMenuEvent *e) {
    QMenu menu(this);
    menu.addAction(QStringLiteral("复制"), this, &TerminalWidget::copy);
    menu.addAction(QStringLiteral("粘贴"), this, &TerminalWidget::paste);
    menu.exec(e->globalPos());
}
} // namespace wise
