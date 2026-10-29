import React from 'react';
import { ServerTelemetry, AuthUser } from '../types';
import { 
  ShieldCheck, 
  Key, 
  Copy, 
  Check, 
  Globe, 
  Heart
} from 'lucide-react';

interface SettingsTabProps {
  telemetry: ServerTelemetry | null;
  user: AuthUser | null;
}

export const SettingsTab: React.FC<SettingsTabProps> = ({ telemetry }) => {
  const [copied1, setCopied1] = React.useState(false);
  const [copied2, setCopied2] = React.useState(false);

  const cmdSetup = 'docker run --rm -it -v steam-session:/data/steam-session sdvd/steam-service:preview setup';
  const cmdExport = 'docker run --rm -v steam-session:/data/steam-session sdvd/steam-service:preview export-token';

  const copyToClipboard = (text: string, setFn: (val: boolean) => void) => {
    navigator.clipboard.writeText(text);
    setFn(true);
    setTimeout(() => setFn(false), 2000);
  };

  return (
    <div className="space-y-6">
      {/* License & Anti-Piracy Compliance Banner */}
      <div className="bg-emerald-50 dark:bg-emerald-950/30 border border-emerald-200 dark:border-emerald-500/30 rounded-xl p-5 transition-colors">
        <div className="flex items-start space-x-3.5">
          <div className="p-2.5 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 rounded-lg border border-emerald-500/20 flex-shrink-0">
            <ShieldCheck className="w-6 h-6" />
          </div>
          <div>
            <div className="flex items-center space-x-2">
              <h3 className="font-bold text-emerald-800 dark:text-emerald-300 text-sm">Kepatuhan Lisensi Asli (Zero-Piracy Compliance)</h3>
              <span className="text-[10px] font-bold uppercase px-2 py-0.5 rounded bg-emerald-500/20 text-emerald-800 dark:text-emerald-300">
                100% Genuine Valve Steam
              </span>
            </div>
            <p className="text-xs text-slate-700 dark:text-slate-300 mt-1 leading-relaxed">
              Dedicated server ini berjalan dengan dependensi resmi Stardew Valley (AppID <code className="text-emerald-600 dark:text-emerald-400 font-mono">413150</code>) yang diunduh langsung dari CDN Valve. Kami menghormati hak cipta kreator game <strong>ConcernedApe (Eric Barone)</strong> dan melarang pendistribusian aset bajakan.
            </p>
          </div>
        </div>
      </div>

      {/* Steam Token Setup Guide */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-6 shadow-sm transition-colors">
        <div className="flex items-center space-x-3 mb-4">
          <div className="p-2 bg-blue-500/10 text-blue-600 dark:text-blue-400 rounded-lg border border-blue-500/20">
            <Key className="w-5 h-5" />
          </div>
          <div>
            <h3 className="font-bold text-slate-900 dark:text-slate-100 text-sm">Konfigurasi STEAM_REFRESH_TOKEN Operator</h3>
            <p className="text-xs text-slate-500 dark:text-slate-400">
              Dua perintah Docker resmi di terminal host VPS Anda untuk memperbarui token unduhan game:
            </p>
          </div>
        </div>

        <div className="space-y-4 pt-2">
          {/* Step 1 */}
          <div>
            <div className="text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1.5 flex items-center space-x-1.5">
              <span className="w-4 h-4 rounded-full bg-slate-100 dark:bg-slate-800 border border-slate-300 dark:border-slate-700 flex items-center justify-center text-[10px] text-slate-700 dark:text-slate-300 font-bold">1</span>
              <span>Login Interaktif &amp; Steam Guard 2FA (Setup Session):</span>
            </div>
            <div className="flex items-center justify-between bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800 rounded-lg p-2.5 font-mono text-xs text-slate-800 dark:text-slate-200">
              <span className="overflow-x-auto whitespace-nowrap mr-2">{cmdSetup}</span>
              <button
                onClick={() => copyToClipboard(cmdSetup, setCopied1)}
                className="p-1.5 rounded bg-slate-200 dark:bg-slate-800 hover:bg-slate-300 dark:hover:bg-slate-700 text-slate-700 dark:text-slate-300 hover:text-slate-900 dark:hover:text-white transition-colors flex-shrink-0"
                title="Salin Perintah"
              >
                {copied1 ? <Check className="w-3.5 h-3.5 text-emerald-600 dark:text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
              </button>
            </div>
          </div>

          {/* Step 2 */}
          <div>
            <div className="text-xs font-semibold text-slate-700 dark:text-slate-300 mb-1.5 flex items-center space-x-1.5">
              <span className="w-4 h-4 rounded-full bg-slate-100 dark:bg-slate-800 border border-slate-300 dark:border-slate-700 flex items-center justify-center text-[10px] text-slate-700 dark:text-slate-300 font-bold">2</span>
              <span>Lihat &amp; Ekspor Token Unduhan (Export Token):</span>
            </div>
            <div className="flex items-center justify-between bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800 rounded-lg p-2.5 font-mono text-xs text-slate-800 dark:text-slate-200">
              <span className="overflow-x-auto whitespace-nowrap mr-2">{cmdExport}</span>
              <button
                onClick={() => copyToClipboard(cmdExport, setCopied2)}
                className="p-1.5 rounded bg-slate-200 dark:bg-slate-800 hover:bg-slate-300 dark:hover:bg-slate-700 text-slate-700 dark:text-slate-300 hover:text-slate-900 dark:hover:text-white transition-colors flex-shrink-0"
                title="Salin Perintah"
              >
                {copied2 ? <Check className="w-3.5 h-3.5 text-emerald-600 dark:text-emerald-400" /> : <Copy className="w-3.5 h-3.5" />}
              </button>
            </div>
            <p className="text-[11px] text-slate-500 mt-1.5">
              Salin token yang dihasilkan dan masukkan ke file <code className="text-slate-700 dark:text-slate-400">.env</code> sebagai <code className="text-emerald-600 dark:text-emerald-400 font-mono">STEAM_REFRESH_TOKEN=...</code>
            </p>
          </div>
        </div>
      </div>

      {/* Network & Ports Info */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-xl p-6 shadow-sm transition-colors">
        <h3 className="font-bold text-slate-900 dark:text-slate-100 text-sm flex items-center space-x-2 mb-4">
          <Globe className="w-4 h-4 text-emerald-600 dark:text-emerald-400" />
          <span>Konfigurasi Jaringan Server (Port Binding)</span>
        </h3>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 text-xs">
          <div className="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800 p-4 rounded-lg">
            <div className="text-slate-500 dark:text-slate-400 font-medium">Port Multiplayer Game (UDP)</div>
            <div className="text-xl font-mono font-bold text-slate-900 dark:text-slate-100 mt-1">
              :{telemetry?.port || 24642} <span className="text-xs font-normal text-emerald-600 dark:text-emerald-400 font-sans">(Terbuka untuk Client)</span>
            </div>
            <p className="text-[11px] text-slate-500 mt-2">
              Digunakan oleh client game dan mod StardewBridge untuk sinkronisasi packet Lidgren UDP.
            </p>
          </div>

          <div className="bg-slate-50 dark:bg-slate-950 border border-slate-200 dark:border-slate-800 p-4 rounded-lg">
            <div className="text-slate-500 dark:text-slate-400 font-medium">Port Web Control Panel (TCP)</div>
            <div className="text-xl font-mono font-bold text-slate-900 dark:text-slate-100 mt-1">
              :{telemetry?.webPort || 5000} <span className="text-xs font-normal text-blue-600 dark:text-blue-400 font-sans">(Kestrel HTTP/WS)</span>
            </div>
            <p className="text-[11px] text-slate-500 mt-2">
              Menyajikan dashboard ini, REST API, dan stream logs WebSocket real-time.
            </p>
          </div>
        </div>
      </div>

      {/* Footer Tribute */}
      <div className="text-center py-4 border-t border-slate-200 dark:border-slate-800/60 text-xs text-slate-500 flex items-center justify-center space-x-1.5">
        <span>LiteValley Self-Host Edition. Dibuat dengan</span>
        <Heart className="w-3.5 h-3.5 text-rose-500 fill-rose-500 inline" />
        <span>sebagai penghormatan untuk Eric Barone &amp; Komunitas SMAPI.</span>
      </div>
    </div>
  );
};
