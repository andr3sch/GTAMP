using System;
using System.Collections.Generic;
using System.Windows.Forms;
using GTAMP.Shared.Packets;

namespace GTAMP.Client
{
    /// <summary>
    /// Menu interativo leve estilo NativeUI (100% compatível com GTA V 1.0.463.1 / SHVDN 2.x).
    /// Controlado pelo servidor/resources via PlayerMenuPacket e MenuSelectPacket.
    /// Navegacao: Setas Cima/Baixo, Enter para confirmar, Backspace/Esc para fechar.
    /// </summary>
    internal sealed class NativeMenuOverlay
    {
        private string _menuId = "";
        private string _title = "";
        private string _subtitle = "";
        private List<MenuItemData> _items = new List<MenuItemData>();
        private int _selectedIndex;

        public bool IsOpen { get; private set; }

        public void ApplyPacket(PlayerMenuPacket pkt)
        {
            if (pkt == null || pkt.Close || pkt.Items == null || pkt.Items.Count == 0)
            {
                Close();
                return;
            }

            _menuId = pkt.MenuId ?? "";
            _title = pkt.Title ?? "Menu";
            _subtitle = pkt.Subtitle ?? "";
            _items = pkt.Items;
            _selectedIndex = 0;
            IsOpen = true;
        }

        public void Close()
        {
            IsOpen = false;
            _items.Clear();
            _selectedIndex = 0;
        }

        /// <summary>
        /// Processa teclas quando o menu esta aberto. Retorna MenuSelectPacket se o usuario confirmou uma opcao.
        /// </summary>
        public MenuSelectPacket HandleKeyDown(KeyEventArgs e)
        {
            if (!IsOpen)
                return null;

            if (e.KeyCode == Keys.Up)
            {
                _selectedIndex--;
                if (_selectedIndex < 0) _selectedIndex = _items.Count - 1;
                return null;
            }

            if (e.KeyCode == Keys.Down)
            {
                _selectedIndex++;
                if (_selectedIndex >= _items.Count) _selectedIndex = 0;
                return null;
            }

            if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Escape)
            {
                Close();
                return null;
            }

            if (e.KeyCode == Keys.Enter && _selectedIndex >= 0 && _selectedIndex < _items.Count)
            {
                MenuItemData chosen = _items[_selectedIndex];
                return new MenuSelectPacket
                {
                    MenuId = _menuId,
                    ItemId = chosen.Id
                };
            }

            return null;
        }

        public void Draw()
        {
            if (!IsOpen || _items.Count == 0)
                return;

            // Bloqueia celular (seta cima), pause (Esc) e menu padrao enquanto o menu GTAMP esta focado
            Natives.DisableMenuConflictControls();

            float x = 0.145f;
            float w = 0.23f;
            float y = 0.14f;

            // Cabecalho azul estilo FiveM/NativeUI
            Natives.DrawRect(x, y, w, 0.055f, 24, 92, 175, 225);
            Natives.DrawText2D(_title, x, y - 0.017f, 0.48f, 255, 255, 255, 255, true, true);
            y += 0.042f;

            // Subtitulo
            Natives.DrawRect(x, y, w, 0.028f, 15, 18, 24, 235);
            Natives.DrawText2D(_subtitle.ToUpperInvariant(), x - w * 0.46f, y - 0.010f, 0.26f, 150, 205, 255, 240, false, false);
            Natives.DrawText2D((_selectedIndex + 1) + "/" + _items.Count, x + w * 0.36f, y - 0.010f, 0.26f, 200, 200, 200, 220, false, false);
            y += 0.029f;

            // Mostra ate 10 itens por vez (janela rolavel)
            int maxVisible = 10;
            int start = 0;
            if (_selectedIndex >= maxVisible)
                start = _selectedIndex - maxVisible + 1;
            int end = Math.Min(_items.Count, start + maxVisible);

            for (int i = start; i < end; i++)
            {
                bool sel = i == _selectedIndex;
                MenuItemData item = _items[i];

                if (sel)
                    Natives.DrawRect(x, y, w, 0.030f, 240, 245, 255, 235);
                else
                    Natives.DrawRect(x, y, w, 0.030f, 18, 21, 28, 195);

                int tr = sel ? 15 : 240;
                int tg = sel ? 18 : 240;
                int tb = sel ? 24 : 240;

                Natives.DrawText2D(item.Label, x - w * 0.46f, y - 0.011f, 0.30f, tr, tg, tb, 255, false, !sel);

                if (!string.IsNullOrEmpty(item.RightLabel))
                {
                    int rr = sel ? 30 : 120;
                    int rg = sel ? 95 : 220;
                    int rb = sel ? 180 : 140;
                    Natives.DrawText2D(item.RightLabel, x + w * 0.28f, y - 0.011f, 0.28f, rr, rg, rb, 245, false, !sel);
                }

                y += 0.031f;
            }

            // Rodape de ajuda
            Natives.DrawRect(x, y + 0.002f, w, 0.024f, 12, 14, 18, 215);
            Natives.DrawText2D("Setas: Navegar | Enter: Selecionar | Back: Sair", x, y - 0.007f, 0.22f, 180, 190, 205, 220, true, false);
        }
    }
}
