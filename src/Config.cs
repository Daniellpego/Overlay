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

        public static string Caminho
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini"); }
        }

        public static Config Carregar()
        {
            var c = new Config();
            if (!File.Exists(Caminho)) return c;

            foreach (string bruta in File.ReadAllLines(Caminho))
            {
                string linha = bruta.Trim();
                if (linha.Length == 0 || linha[0] == '#' || linha[0] == ';') continue;

                int igual = linha.IndexOf('=');
                if (igual <= 0) continue;

                string chave = linha.Substring(0, igual).Trim().ToLowerInvariant();
                string valor = linha.Substring(igual + 1).Trim();
                c.Aplicar(chave, valor);
            }
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
            }
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
