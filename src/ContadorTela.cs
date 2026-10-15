using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace Overlay
{
    // Plano B do FPS: conta quantas vezes a imagem do JOGO muda por segundo, pela API Desktop Duplication do Windows
    // (a mesma do OBS e do compartilhamento de tela do Discord). Não toca no processo do jogo, não usa ETW e não precisa
    // de admin; serve quando o anticheat (ex.: EA Javelin no FC 27) faz o Windows recusar as sessões ETW do PresentMon.
    // A cada atualização da tela o Windows diz QUAIS retângulos mudaram; só conta se a mudança cai dentro da janela do jogo
    // e fora da área ignorada (o próprio overlay). Assim outras janelas e os redesenhos do overlay não somam no FPS.
    // Limite: mede o que chega ao monitor (teto = taxa de atualização da tela).
    // Chamadas COM feitas pela vtable (índices do SDK do Windows; offsets x64, build /platform:x64).
    class ContadorTela : IDisposable
    {
        const int DXGI_ERROR_WAIT_TIMEOUT = unchecked((int)0x887A0027);
        const int DXGI_ERROR_ACCESS_LOST = unchecked((int)0x887A0026);
        const int D3D_DRIVER_TYPE_HARDWARE = 1;
        const uint D3D11_SDK_VERSION = 7;
        const double JanelaMs = 1000;
        const double ValidadeMs = 1500;
        const int TamanhoRetanguloMovido = 24; // DXGI_OUTDUPL_MOVE_RECT: POINT origem + RECT destino
        const int TamanhoRetangulo = 16;        // RECT

        // Índices na vtable (IUnknown 0–2, IDXGIObject 3–6).
        const int VtQueryInterface = 0;
        const int VtRelease = 2;
        const int VtDeviceGetAdapter = 7;
        const int VtAdapterEnumOutputs = 7;
        const int VtOutputGetDesc = 7;
        const int VtOutput1DuplicateOutput = 22;
        const int VtDuplicationAcquireNextFrame = 8;
        const int VtDuplicationGetFrameDirtyRects = 9;
        const int VtDuplicationGetFrameMoveRects = 10;
        const int VtDuplicationReleaseFrame = 14;

        static readonly Guid IidDxgiDevice = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
        // IID conferido no dxgi1_2.idl (Wine), que espelha o dxgi1_2.h do SDK.
        static readonly Guid IidDxgiOutput1 = new Guid("00cddea8-939b-4b83-a340-a685226666cc");

        [DllImport("d3d11.dll")]
        static extern int D3D11CreateDevice(IntPtr adaptador, int tipo, IntPtr software, uint flags, IntPtr niveis,
            uint quantidadeNiveis, uint versaoSdk, out IntPtr dispositivo, out int nivel, out IntPtr contexto);
        [DllImport("kernel32.dll")] static extern bool QueryPerformanceFrequency(out long frequencia);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr janela, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr janela, out RECT r);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnQueryInterface(IntPtr obj, ref Guid iid, out IntPtr saida);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint FnRelease(IntPtr obj);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnGetAdapter(IntPtr obj, out IntPtr adaptador);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnEnumOutputs(IntPtr obj, uint indice, out IntPtr saida);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnGetDesc(IntPtr obj, IntPtr desc);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnDuplicateOutput(IntPtr obj, IntPtr dispositivo, out IntPtr duplicacao);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnAcquireNextFrame(IntPtr obj, uint timeoutMs, IntPtr info, out IntPtr recurso);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnGetFrameRects(IntPtr obj, uint tamanho, IntPtr buffer, out uint necessario);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int FnReleaseFrame(IntPtr obj);

        readonly object trava = new object();
        readonly Queue<KeyValuePair<long, int>> quadros = new Queue<KeyValuePair<long, int>>();
        readonly ManualResetEvent parar = new ManualResetEvent(false);
        readonly long frequenciaQpc;
        readonly Func<IntPtr> janelaAlvo;
        Thread thread;
        volatile bool encerrando;
        long ultimoQuadroQpc;
        // Área a ignorar em coordenadas de tela (o próprio overlay). Lida pela thread do contador; trocada inteira.
        Rectangle areaIgnorada;

        public string UltimoErro { get; private set; }

        // janelaAlvo: janela do jogo (padrão: a janela em primeiro plano, que no jogo é o próprio jogo).
        public ContadorTela(Func<IntPtr> janelaAlvo = null)
        {
            QueryPerformanceFrequency(out frequenciaQpc);
            this.janelaAlvo = janelaAlvo ?? GetForegroundWindow;
        }

        public void Iniciar()
        {
            thread = new Thread(Loop) { IsBackground = true, Name = "ContadorTela" };
            thread.Start();
        }

        // O overlay informa onde está a cada redesenho: mudanças só nessa área não contam como quadro do jogo.
        public void DefinirAreaIgnorada(Rectangle area)
        {
            lock (trava) areaIgnorada = area;
        }

        // Quadros por segundo do jogo, ou null sem dados recentes.
        public int? Fps()
        {
            long agora = Qpc();
            lock (trava)
            {
                if (quadros.Count == 0 || MsEntre(ultimoQuadroQpc, agora) > ValidadeMs) return null;
                Podar(agora);
                int total = 0;
                foreach (KeyValuePair<long, int> q in quadros) total += q.Value;
                return total;
            }
        }

        void Loop()
        {
            int esperaMs = 1000;
            while (!encerrando)
            {
                try
                {
                    Duplicar();
                    esperaMs = 1000;
                }
                catch (Exception ex)
                {
                    UltimoErro = ex.Message;
                    Log.Mensagem("ContadorTela: " + ex.Message);
                }
                if (parar.WaitOne(esperaMs)) break;
                esperaMs = Math.Min(esperaMs * 2, 30000);
            }
        }

        // Cria a duplicação do monitor onde está a janela do jogo e conta quadros até perder o acesso
        // (troca de resolução, tela de UAC, tela cheia exclusiva) ou o jogo ir para outro monitor.
        void Duplicar()
        {
            IntPtr dispositivo = IntPtr.Zero, contexto = IntPtr.Zero, dxgiDevice = IntPtr.Zero, adaptador = IntPtr.Zero;
            IntPtr saida = IntPtr.Zero, saida1 = IntPtr.Zero, duplicacao = IntPtr.Zero;
            IntPtr info = Marshal.AllocHGlobal(48);   // DXGI_OUTDUPL_FRAME_INFO
            IntPtr desc = Marshal.AllocHGlobal(96);   // DXGI_OUTPUT_DESC
            int tamanhoMetadados = 4096;
            IntPtr metadados = Marshal.AllocHGlobal(tamanhoMetadados);
            try
            {
                int nivel;
                Checar(D3D11CreateDevice(IntPtr.Zero, D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero, 0, IntPtr.Zero, 0,
                    D3D11_SDK_VERSION, out dispositivo, out nivel, out contexto), "D3D11CreateDevice");
                Guid iid = IidDxgiDevice;
                Checar(Metodo<FnQueryInterface>(dispositivo, VtQueryInterface)(dispositivo, ref iid, out dxgiDevice), "IDXGIDevice");
                Checar(Metodo<FnGetAdapter>(dxgiDevice, VtDeviceGetAdapter)(dxgiDevice, out adaptador), "GetAdapter");

                IntPtr monitorAlvo = MonitorFromWindow(janelaAlvo(), 2 /* MONITOR_DEFAULTTONEAREST */);
                IntPtr monitorDuplicado = IntPtr.Zero;
                Point origemMonitor = Point.Empty;
                for (uint i = 0; ; i++)
                {
                    IntPtr candidata;
                    if (Metodo<FnEnumOutputs>(adaptador, VtAdapterEnumOutputs)(adaptador, i, out candidata) != 0) break;
                    Checar(Metodo<FnGetDesc>(candidata, VtOutputGetDesc)(candidata, desc), "GetDesc");
                    IntPtr monitor = Marshal.ReadIntPtr(desc, 88); // DXGI_OUTPUT_DESC.Monitor
                    // Fica com o monitor do jogo; se não achar, com o primeiro da placa.
                    if (saida == IntPtr.Zero || monitor == monitorAlvo)
                    {
                        Liberar(ref saida);
                        saida = candidata;
                        monitorDuplicado = monitor;
                        // DXGI_OUTPUT_DESC.DesktopCoordinates: os retângulos que mudaram vêm relativos a esse canto.
                        origemMonitor = new Point(Marshal.ReadInt32(desc, 64), Marshal.ReadInt32(desc, 68));
                        if (monitor == monitorAlvo) break;
                    }
                    else Liberar(ref candidata);
                }
                if (saida == IntPtr.Zero) throw new InvalidOperationException("nenhum monitor ligado a placa de video principal");

                iid = IidDxgiOutput1;
                Checar(Metodo<FnQueryInterface>(saida, VtQueryInterface)(saida, ref iid, out saida1), "IDXGIOutput1");
                Checar(Metodo<FnDuplicateOutput>(saida1, VtOutput1DuplicateOutput)(saida1, dispositivo, out duplicacao), "DuplicateOutput");
                UltimoErro = null;

                FnAcquireNextFrame adquirir = Metodo<FnAcquireNextFrame>(duplicacao, VtDuplicationAcquireNextFrame);
                FnGetFrameRects movidos = Metodo<FnGetFrameRects>(duplicacao, VtDuplicationGetFrameMoveRects);
                FnGetFrameRects sujos = Metodo<FnGetFrameRects>(duplicacao, VtDuplicationGetFrameDirtyRects);
                FnReleaseFrame soltar = Metodo<FnReleaseFrame>(duplicacao, VtDuplicationReleaseFrame);

                Rectangle areaJogo = AreaDoJogo(origemMonitor);
                long proximaChecagem = Qpc() + frequenciaQpc / 4;
                long proximaChecagemMonitor = Qpc() + frequenciaQpc;
                while (!encerrando)
                {
                    IntPtr recurso;
                    int hr = adquirir(duplicacao, 250, info, out recurso);
                    if (hr == DXGI_ERROR_WAIT_TIMEOUT) { }
                    else if (hr == DXGI_ERROR_ACCESS_LOST) return;
                    else
                    {
                        Checar(hr, "AcquireNextFrame");
                        long ultimoPresent = Marshal.ReadInt64(info, 0);
                        int acumulados = Marshal.ReadInt32(info, 16);
                        int tamanhoNecessario = Marshal.ReadInt32(info, 40);
                        // LastPresentTime = 0 é só o ponteiro do mouse se mexendo: não é quadro novo.
                        if (ultimoPresent != 0 && acumulados > 0)
                        {
                            if (tamanhoNecessario > tamanhoMetadados)
                            {
                                Marshal.FreeHGlobal(metadados);
                                tamanhoMetadados = tamanhoNecessario;
                                metadados = Marshal.AllocHGlobal(tamanhoMetadados);
                            }
                            Rectangle ignorar;
                            lock (trava) ignorar = areaIgnorada;
                            ignorar.Offset(-origemMonitor.X, -origemMonitor.Y);
                            ignorar.Inflate(2, 2);
                            if (tamanhoNecessario == 0 ||
                                MudouNoJogo(duplicacao, movidos, sujos, metadados, tamanhoMetadados, areaJogo, ignorar))
                                Registrar(ultimoPresent, acumulados);
                        }
                        if (recurso != IntPtr.Zero) Liberar(ref recurso);
                        soltar(duplicacao);
                    }

                    long agora = Qpc();
                    if (agora >= proximaChecagem)
                    {
                        // A janela do jogo pode mudar de tamanho/posição (ou o jogo da frente pode mudar).
                        proximaChecagem = agora + frequenciaQpc / 4;
                        areaJogo = AreaDoJogo(origemMonitor);
                    }
                    if (agora >= proximaChecagemMonitor)
                    {
                        // Jogo foi para outro monitor: recria a duplicação no monitor certo.
                        proximaChecagemMonitor = agora + frequenciaQpc;
                        if (MonitorFromWindow(janelaAlvo(), 2) != monitorDuplicado) return;
                    }
                }
            }
            finally
            {
                Liberar(ref duplicacao);
                Liberar(ref saida1);
                Liberar(ref saida);
                Liberar(ref adaptador);
                Liberar(ref dxgiDevice);
                Liberar(ref contexto);
                Liberar(ref dispositivo);
                Marshal.FreeHGlobal(metadados);
                Marshal.FreeHGlobal(desc);
                Marshal.FreeHGlobal(info);
            }
        }

        // Janela do jogo em coordenadas do monitor duplicado. Vazia = vale o monitor inteiro.
        Rectangle AreaDoJogo(Point origemMonitor)
        {
            RECT r;
            if (!GetWindowRect(janelaAlvo(), out r) || r.Right <= r.Left || r.Bottom <= r.Top) return Rectangle.Empty;
            return Rectangle.FromLTRB(r.Left - origemMonitor.X, r.Top - origemMonitor.Y, r.Right - origemMonitor.X, r.Bottom - origemMonitor.Y);
        }

        // Algum retângulo movido ou "sujo" (redesenhado) caiu dentro do jogo e fora da área ignorada?
        static bool MudouNoJogo(IntPtr duplicacao, FnGetFrameRects movidos, FnGetFrameRects sujos, IntPtr buffer, int tamanho,
            Rectangle areaJogo, Rectangle ignorar)
        {
            uint usados;
            if (movidos(duplicacao, (uint)tamanho, buffer, out usados) >= 0)
                for (int i = 0; i + TamanhoRetanguloMovido <= usados; i += TamanhoRetanguloMovido)
                    if (Relevante(LerRetangulo(buffer, i + 8), areaJogo, ignorar)) return true;
            if (sujos(duplicacao, (uint)tamanho, buffer, out usados) >= 0)
                for (int i = 0; i + TamanhoRetangulo <= usados; i += TamanhoRetangulo)
                    if (Relevante(LerRetangulo(buffer, i), areaJogo, ignorar)) return true;
            return false;
        }

        // Público para teste (tests\TesteContadorTela.cs): a regra de "isso é quadro do jogo?".
        public static bool Relevante(Rectangle mudanca, Rectangle areaJogo, Rectangle ignorar)
        {
            Rectangle dentro = areaJogo.IsEmpty ? mudanca : Rectangle.Intersect(mudanca, areaJogo);
            if (dentro.Width <= 0 || dentro.Height <= 0) return false;
            return ignorar.IsEmpty || !ignorar.Contains(dentro);
        }

        static Rectangle LerRetangulo(IntPtr buffer, int deslocamento)
        {
            return Rectangle.FromLTRB(Marshal.ReadInt32(buffer, deslocamento), Marshal.ReadInt32(buffer, deslocamento + 4),
                Marshal.ReadInt32(buffer, deslocamento + 8), Marshal.ReadInt32(buffer, deslocamento + 12));
        }

        void Registrar(long qpc, int acumulados)
        {
            lock (trava)
            {
                quadros.Enqueue(new KeyValuePair<long, int>(qpc, acumulados));
                ultimoQuadroQpc = Math.Max(ultimoQuadroQpc, qpc);
                Podar(Qpc());
            }
        }

        void Podar(long agora)
        {
            while (quadros.Count > 0 && MsEntre(quadros.Peek().Key, agora) > JanelaMs) quadros.Dequeue();
        }

        double MsEntre(long de, long ate)
        {
            return (ate - de) * 1000.0 / frequenciaQpc;
        }

        static long Qpc()
        {
            return System.Diagnostics.Stopwatch.GetTimestamp();
        }

        static T Metodo<T>(IntPtr objeto, int indice) where T : class
        {
            IntPtr vtable = Marshal.ReadIntPtr(objeto);
            IntPtr funcao = Marshal.ReadIntPtr(vtable, indice * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer(funcao, typeof(T)) as T;
        }

        static void Liberar(ref IntPtr objeto)
        {
            if (objeto == IntPtr.Zero) return;
            Metodo<FnRelease>(objeto, VtRelease)(objeto);
            objeto = IntPtr.Zero;
        }

        static void Checar(int hr, string chamada)
        {
            if (hr < 0) throw new InvalidOperationException(chamada + " falhou (0x" + hr.ToString("X8") + ")");
        }

        public void Dispose()
        {
            if (encerrando) return;
            encerrando = true;
            parar.Set();
            if (thread != null) thread.Join(1000);
        }
    }
}
