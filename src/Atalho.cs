using System;
using System.Windows.Forms;

namespace Overlay
{
    // Converte "Ctrl+Shift+F10" no par (modificadores, tecla) do RegisterHotKey. Testado em tests\TesteEtapa4.cs.
    static class Atalho
    {
        public const uint MOD_ALT = 0x1;
        public const uint MOD_CONTROL = 0x2;
        public const uint MOD_SHIFT = 0x4;
        public const uint MOD_WIN = 0x8;
        public const uint MOD_NOREPEAT = 0x4000;

        public static bool TentarLer(string texto, out uint modificadores, out uint tecla)
        {
            modificadores = 0;
            tecla = 0;
            if (string.IsNullOrEmpty(texto)) return false;

            foreach (string bruta in texto.Split('+'))
            {
                string parte = bruta.Trim().ToLowerInvariant();
                if (parte.Length == 0) return false;

                if (parte == "ctrl" || parte == "control") modificadores |= MOD_CONTROL;
                else if (parte == "shift") modificadores |= MOD_SHIFT;
                else if (parte == "alt") modificadores |= MOD_ALT;
                else if (parte == "win") modificadores |= MOD_WIN;
                else
                {
                    if (tecla != 0) return false; // duas teclas comuns ("A+B") não formam atalho
                    // Enum.TryParse aceitaria "5" como o valor numérico 5 (botão do mouse) e "a,b" como combinação.
                    if (parte.Length == 1 && char.IsDigit(parte[0])) parte = "d" + parte;
                    else if (char.IsDigit(parte[0]) || parte.IndexOf(',') >= 0) return false;
                    Keys k;
                    if (!Enum.TryParse(parte, true, out k) || (k & Keys.Modifiers) != 0 || k == Keys.None) return false;
                    tecla = (uint)k;
                }
            }
            // Sem modificador, uma tecla comum (ex.: "F") seria engolida do jogo; só F1–F24 podem ir sozinhas.
            if (tecla == 0) return false;
            bool teclaF = tecla >= (uint)Keys.F1 && tecla <= (uint)Keys.F24;
            if (modificadores == 0 && !teclaF) return false;

            modificadores |= MOD_NOREPEAT;
            return true;
        }
    }
}
