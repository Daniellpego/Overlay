using System;
using System.Drawing;
using System.Globalization;
using System.IO;

namespace Overlay
{
    // Valores inválidos no config.ini são ignorados e o padrão é mantido: o overlay nunca deve deixar de abrir por causa de um erro de digitação.
    class Config
    {
        public string Canto = "superior-direito";
        public int MargemX = 12;
        public int MargemY = 12;
        public int Monitor = 0;
        public string Fonte = "Consolas";
        public float TamanhoFonte = 11f;
        public Color CorTexto = Color.White;
        public int OpacidadeFundo = 140;
        public bool MostrarSegundos = false;
        public string AlvoPing = "1.1.1.1";
        public bool DetectarServidorJogo = true;
        public string AtalhoOcultar = "Ctrl+Shift+F10";
        public bool CoresPorFaixa = true;
        public int FpsBom = 60;
        public int FpsRuim = 30;
        public int PingBom = 60;
        public int PingRuim = 120;
        public Color CorBom = Color.FromArgb(76, 217, 100);
        public Color CorMedio = Color.FromArgb(255, 214, 10);
        public Color CorRuim = Color.FromArgb(255, 69, 58);

        public static string Caminho
        {
            get { return Path.Combine(Pastas.Config, "config.ini"); }
        }

        public static Config Carregar()
        {
            var c = new Config();
            string[] linhas;
            try
            {
                if (!File.Exists(Caminho)) return c;
                linhas = File.ReadAllLines(Caminho);
            }
            catch (Exception ex)
            {
                // Config bloqueado por outro programa ou sem permissão: abre com o padrão em vez de não abrir.
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                Log.Mensagem("Nao foi possivel ler config.ini (" + ex.Message + "); usando o padrao.");
                return c;
            }

            foreach (string bruta in linhas)
            {
                string linha = bruta.Trim();
                if (linha.Length == 0 || linha[0] == '#' || linha[0] == ';') continue;

                int igual = linha.IndexOf('=');
                if (igual <= 0) continue;

                string chave = linha.Substring(0, igual).Trim().ToLowerInvariant();
                string valor = linha.Substring(igual + 1).Trim();
                c.Aplicar(chave, valor);
            }
            c.CorrigirFaixas();
            return c;
        }

        void Aplicar(string chave, string valor)
        {
            switch (chave)
            {
                case "canto":
                    string canto = valor.ToLowerInvariant();
                    if (canto == "superior-esquerdo" || canto == "superior-direito" ||
                        canto == "inferior-esquerdo" || canto == "inferior-direito")
                        Canto = canto;
                    break;
                case "margem_x": MargemX = Inteiro(valor, MargemX, 0, 5000); break;
                case "margem_y": MargemY = Inteiro(valor, MargemY, 0, 5000); break;
                case "monitor": Monitor = Inteiro(valor, Monitor, 0, 16); break;
                case "fonte": if (valor.Length > 0) Fonte = valor; break;
                case "tamanho_fonte": TamanhoFonte = Inteiro(valor, (int)TamanhoFonte, 6, 72); break;
                case "cor_texto": CorTexto = Cor(valor, CorTexto); break;
                case "opacidade_fundo": OpacidadeFundo = Inteiro(valor, OpacidadeFundo, 0, 255); break;
                case "mostrar_segundos": MostrarSegundos = Booleano(valor, MostrarSegundos); break;
                case "alvo_ping": if (valor.Length > 0) AlvoPing = valor; break;
                case "detectar_servidor_jogo": DetectarServidorJogo = Booleano(valor, DetectarServidorJogo); break;
                case "atalho_ocultar": AtalhoOcultar = valor; break;
                case "cores_por_faixa": CoresPorFaixa = Booleano(valor, CoresPorFaixa); break;
                case "fps_bom": FpsBom = Inteiro(valor, FpsBom, 1, 1000); break;
                case "fps_ruim": FpsRuim = Inteiro(valor, FpsRuim, 1, 1000); break;
                case "ping_bom": PingBom = Inteiro(valor, PingBom, 1, 1000); break;
                case "ping_ruim": PingRuim = Inteiro(valor, PingRuim, 1, 1000); break;
                case "cor_bom": CorBom = Cor(valor, CorBom); break;
                case "cor_medio": CorMedio = Cor(valor, CorMedio); break;
                case "cor_ruim": CorRuim = Cor(valor, CorRuim); break;
            }
        }

        // Faixas invertidas (ex.: fps_ruim maior que fps_bom) tornariam as cores sem sentido: volta ao padrão.
        void CorrigirFaixas()
        {
            var padrao = new Config();
            if (FpsRuim > FpsBom) { FpsBom = padrao.FpsBom; FpsRuim = padrao.FpsRuim; }
            if (PingBom > PingRuim) { PingBom = padrao.PingBom; PingRuim = padrao.PingRuim; }
        }

        static int Inteiro(string valor, int padrao, int min, int max)
        {
            int n;
            if (!int.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return padrao;
            return Math.Max(min, Math.Min(max, n));
        }

        static bool Booleano(string valor, bool padrao)
        {
            string v = valor.ToLowerInvariant();
            if (v == "sim" || v == "true" || v == "1") return true;
            if (v == "nao" || v == "não" || v == "false" || v == "0") return false;
            return padrao;
        }

        static Color Cor(string valor, Color padrao)
        {
            string hex = valor.TrimStart('#');
            int rgb;
            if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)) return padrao;
            return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        }
    }
}
