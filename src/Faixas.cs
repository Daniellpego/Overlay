using System.Drawing;

namespace Overlay
{
    // Cor de cada número conforme as faixas do config.ini. Testado em tests\TesteEtapa4.cs.
    static class Faixas
    {
        public static Color CorFps(int? fps, Config c)
        {
            if (!c.CoresPorFaixa || !fps.HasValue) return c.CorTexto;
            if (fps.Value >= c.FpsBom) return c.CorBom;
            if (fps.Value >= c.FpsRuim) return c.CorMedio;
            return c.CorRuim;
        }

        public static Color CorPing(int? ms, Config c)
        {
            if (!c.CoresPorFaixa || !ms.HasValue) return c.CorTexto;
            if (ms.Value <= c.PingBom) return c.CorBom;
            if (ms.Value <= c.PingRuim) return c.CorMedio;
            return c.CorRuim;
        }
    }
}
