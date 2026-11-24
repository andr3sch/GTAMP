using System;
using System.Collections.Generic;
using System.Windows.Forms;
using GTAMP.Shared;

namespace GTAMP.Client
{
    /// <summary>
    /// Chat simples na tela usando natives 2D de 2015 (sem NativeUI ou bibliotecas externas).
    /// Pressione T para abrir, Enter para enviar, Esc para cancelar.
    /// </summary>
    internal sealed class ChatOverlay
    {
        private sealed class ChatLine
        {
            public string Sender;
            public string Text;
            public bool IsSystem;
            public int TimestampMs;
        }

        private const int MaxLines = 8;
        private const int FadeMs = 12000;

        private readonly List<ChatLine> _lines = new List<ChatLine>();
        private string _input = string.Empty;

        public bool IsTyping { get; private set; }

        public void AddMessage(string sender, string text, bool isSystem)
        {
            _lines.Add(new ChatLine
            {
                Sender = sender ?? string.Empty,
                Text = text ?? string.Empty,
                IsSystem = isSystem,
                TimestampMs = Environment.TickCount
            });

            while (_lines.Count > MaxLines)
                _lines.RemoveAt(0);
        }

        public void OpenInput()
        {
            IsTyping = true;
            _input = string.Empty;
        }

        public void CloseInput()
        {
            IsTyping = false;
            _input = string.Empty;
        }

        /// <summary>
        /// Processa tecla quando o chat esta aberto. Retorna a mensagem pronta se Enter foi pressionado, ou null.
        /// </summary>
        public string HandleKeyDown(KeyEventArgs e)
        {
            if (!IsTyping)
                return null;

            if (e.KeyCode == Keys.Escape)
            {
                CloseInput();
                return null;
            }

            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Return)
            {
                string msg = _input.Trim();
                CloseInput();
                return msg.Length > 0 ? msg : null;
            }

            if (e.KeyCode == Keys.Back)
            {
                if (_input.Length > 0)
                    _input = _input.Substring(0, _input.Length - 1);
                return null;
            }

            if (_input.Length >= Protocol.MaxChatLength)
                return null;

            char c = KeyToChar(e.KeyCode, e.Shift);
            if (c != '\0')
                _input += c;

            return null;
        }

        public void Draw()
        {
            if (IsTyping)
                Natives.DisableAllControlActions();

            int now = Environment.TickCount;
            float x = 0.015f;
            float y = 0.03f;
            float lineHeight = 0.025f;

            // Conta linhas visiveis
            int visible = 0;
            for (int i = 0; i < _lines.Count; i++)
            {
                if (IsTyping || unchecked(now - _lines[i].TimestampMs) < FadeMs)
                    visible++;
            }

            if (visible > 0)
            {
                float boxH = visible * lineHeight + 0.012f;
                Natives.DrawRect(x + 0.18f, y + boxH * 0.5f - 0.004f, 0.37f, boxH, 0, 0, 0, 110);

                float cy = y;
                for (int i = 0; i < _lines.Count; i++)
                {
                    ChatLine line = _lines[i];
                    if (!IsTyping && unchecked(now - line.TimestampMs) >= FadeMs)
                        continue;

                    string formatted = line.IsSystem
                        ? ("* " + line.Text)
                        : (line.Sender + ": " + line.Text);

                    if (line.IsSystem)
                        Natives.DrawText2D(formatted, x + 0.004f, cy, 0.30f, 130, 210, 255, 240, false, true);
                    else
                        Natives.DrawText2D(formatted, x + 0.004f, cy, 0.30f, 255, 255, 255, 240, false, true);

                    cy += lineHeight;
                }
            }

            if (IsTyping)
            {
                float inputY = y + visible * lineHeight + 0.018f;
                Natives.DrawRect(x + 0.18f, inputY + 0.012f, 0.37f, 0.030f, 0, 0, 0, 175);
                Natives.DrawText2D("> " + _input + "_", x + 0.006f, inputY, 0.32f, 255, 255, 180, 255, false, true);
            }
        }

        private static char KeyToChar(Keys key, bool shift)
        {
            if (key >= Keys.A && key <= Keys.Z)
            {
                char c = (char)('a' + (key - Keys.A));
                return shift ? char.ToUpperInvariant(c) : c;
            }
            if (key >= Keys.D0 && key <= Keys.D9)
            {
                const string shifted = ")!@#$%^&*(";
                int idx = key - Keys.D0;
                return shift ? shifted[idx] : (char)('0' + idx);
            }
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
                return (char)('0' + (key - Keys.NumPad0));

            switch (key)
            {
                case Keys.Space: return ' ';
                case Keys.OemPeriod:
                case Keys.Decimal: return shift ? '>' : '.';
                case Keys.Oemcomma: return shift ? '<' : ',';
                case Keys.OemMinus:
                case Keys.Subtract: return shift ? '_' : '-';
                case Keys.Oemplus:
                case Keys.Add: return shift ? '+' : '=';
                case Keys.OemQuestion:
                case Keys.Divide: return shift ? '?' : '/';
                case Keys.Oem1: return shift ? ':' : ';';
                case Keys.OemOpenBrackets: return shift ? '{' : '[';
                case Keys.OemCloseBrackets: return shift ? '}' : ']';
                default: return '\0';
            }
        }
    }
}
