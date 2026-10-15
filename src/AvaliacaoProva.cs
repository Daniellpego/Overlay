using System;

namespace Overlay
{
    static class AvaliacaoProva
    {
        public static bool AmostrasSuficientes(int amostras, int esperadas)
        {
            return esperadas >= 5 && amostras >= 5 && amostras >= Math.Ceiling(esperadas * 0.8);
        }

        public static bool FpsCompativel(int presentMon, int tela, int hz)
        {
            if (presentMon <= 0 || tela <= 0) return false;
            int esperado = hz > 0 ? Math.Min(presentMon, hz) : presentMon;
            return Math.Abs(tela - esperado) <= esperado * 0.05;
        }

        public static int CodigoResultado(int falhas, int inconclusivos)
        {
            return falhas > 0 ? 1 : inconclusivos > 0 ? 2 : 0;
        }
    }
}
