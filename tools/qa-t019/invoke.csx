new System.Func<string>(() => {
    try {
        var asm = System.Reflection.Assembly.LoadFrom("/home/polyphonyrequiem/valheim/qadiag-t019/bin/Release/SBPR.QADiag.T019.dll");
        var t = asm.GetType("SBPR.QADiag.T019.Instrument");
        var m = t.GetMethod("Run", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
        return (string)m.Invoke(null, null);
    } catch (System.Exception ex) { return "INVOKE-FATAL: " + ex + (ex.InnerException!=null? "\nINNER: "+ex.InnerException : ""); }
})();
