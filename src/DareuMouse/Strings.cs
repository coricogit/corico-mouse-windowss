using System.Globalization;

namespace DareuMouse;

/// <summary>Interface text in Spanish or English, following the Windows display language.</summary>
internal static class T
{
    private static readonly bool Es = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es";

    private static string S(string es, string en) => Es ? es : en;

    public static string Title => "Dareu A950";
    public static string Wireless => S("2.4G", "2.4G");
    public static string Wired => S("Cable", "Wired");
    public static string Charging => S("cargando", "charging");
    public static string Loading => S("Buscando el mouse…", "Looking for the mouse…");
    public static string Asleep => S("El mouse está dormido. Muévelo para despertarlo.", "The mouse is asleep. Move it to wake it up.");
    public static string NoDevice => S("No se encontró el receptor Dareu. Conéctalo o conecta el mouse por cable.", "Dareu receiver not found. Plug it in, or connect the mouse with its cable.");
    public static string ErrorPrefix => S("Error: ", "Error: ");
    public static string Dpi => "DPI";
    public static string DpiHint => S("Clic: usar · Clic derecho: editar", "Click: use · Right-click: edit");
    public static string EditingStage(int stage) => S($"Editando el nivel {stage}", $"Editing stage {stage}");
    public static string Polling => S("Frecuencia de sondeo", "Polling rate");
    public static string PollingWarning => S("Por encima de 1000 Hz la batería dura bastante menos.", "Above 1000 Hz the battery drains much faster.");
    public static string WiredPollingNote => S("Por cable el máximo es 1000 Hz.", "Over the cable the maximum is 1000 Hz.");
    public static string Pointer => S("Puntero de Windows", "Windows pointer");
    public static string Speed => S("Velocidad", "Speed");
    public static string SpeedNeutral => S("1:1, sin escalar", "1:1, no scaling");
    public static string SpeedHint => S("Clic derecho: volver a 10", "Right-click: back to 10");
    public static string NoAccel => S("Sin aceleración", "No acceleration");
    public static string PointerNote => S("Desactiva «Mejorar precisión del puntero». Aplica a todos los mouse.", "Turns off \"Enhance pointer precision\". Applies to every mouse.");
    public static string StartWithWindows => S("Iniciar con Windows", "Start with Windows");
    public static string Open => S("Abrir", "Open");
    public static string Refresh => S("Actualizar", "Refresh");
    public static string CopyDiagnostics => S("Copiar diagnóstico", "Copy diagnostics");
    public static string Copied => S("Diagnóstico copiado al portapapeles.", "Diagnostics copied to the clipboard.");
    public static string Exit => S("Salir", "Exit");
    public static string AlreadyRunning => S("Dareu Mouse ya está abierto en la bandeja del sistema.", "Dareu Mouse is already running in the system tray.");
    public static string LowBatteryTitle => S("Batería baja", "Low battery");
    public static string LowBattery(int percent) => S($"Al mouse le queda {percent} %. Conéctalo para cargarlo.", $"The mouse has {percent}% left. Plug it in to charge.");
}
