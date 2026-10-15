using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OverlayDistribuicao {
 public static class Configuracao {
  [StructLayout(LayoutKind.Sequential)] struct Informacao {
   public uint Atributos;
   public System.Runtime.InteropServices.ComTypes.FILETIME Criacao, Acesso, Escrita;
   public uint Volume, TamanhoAlto, TamanhoBaixo, Links, IndiceAlto, IndiceBaixo;
  }
  [DllImport("kernel32.dll", EntryPoint="CreateFileW", CharSet=CharSet.Unicode, SetLastError=true)]
  static extern SafeFileHandle Abrir(string nome, uint acesso, uint compartilhar, IntPtr seguranca, uint modo, uint flags, IntPtr modelo);
  [DllImport("kernel32.dll", EntryPoint="GetFileInformationByHandle", SetLastError=true)]
  static extern bool Informar(SafeFileHandle h, out Informacao i);
  public static void Criar(string origem, string pasta) {
   string destino = Path.GetFullPath(pasta);
   if (destino.StartsWith(@"\\")) throw new IOException("Use uma pasta local para a configuracao.");
   byte[] bytes = File.ReadAllBytes(origem);
   var travas = new List<SafeFileHandle>();
   try {
    string atual = Path.GetPathRoot(destino);
    string[] partes = destino.Substring(atual.Length).Split('\\');
    for (int n = -1; n < partes.Length; n++) {
     if (n >= 0) { atual = Path.Combine(atual, partes[n]); if (!Directory.Exists(atual)) Directory.CreateDirectory(atual); }
     // Sem compartilhar escrita/exclusao: um ancestral nao pode virar juncao durante a criacao.
     SafeFileHandle h = Abrir(atual, 0, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
     if (h.IsInvalid) { h.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
     travas.Add(h);
     Informacao i;
     if (!Informar(h, out i)) throw new Win32Exception(Marshal.GetLastWin32Error());
     if ((i.Atributos & 0x400) != 0) throw new IOException("A configuracao nao pode usar links ou juncoes.");
    }
    string arquivo = Path.Combine(destino, "config.ini");
    if (File.Exists(arquivo)) return;
    FileStream novo;
    try { novo = new FileStream(arquivo, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
    catch (IOException) { if (File.Exists(arquivo)) return; throw; }
    using (novo) novo.Write(bytes, 0, bytes.Length);
   } finally { for (int n = travas.Count-1; n >= 0; n--) travas[n].Dispose(); }
  }
 }
}

namespace OverlayDistribuicao {
 public static class Janelas {
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", EntryPoint="PostMessageW", CharSet=CharSet.Unicode)] static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static void Fechar(uint alvo) {
   EnumWindows((h,l)=>{uint pid; GetWindowThreadProcessId(h,out pid); if(pid==alvo) PostMessage(h,0x0010,IntPtr.Zero,IntPtr.Zero); return true;},IntPtr.Zero);
  }
 }
}
