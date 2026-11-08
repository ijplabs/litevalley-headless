import React, { useState, useEffect } from 'react';
import { AuthUser } from '../types';
import { setStoredAuth } from '../lib/api';
import {
  Lock,
  User,
  KeyRound,
  Mail,
  Briefcase,
  AlertCircle,
  CheckCircle2,
  X,
  UserPlus,
  LogIn,
  ShieldCheck,
} from 'lucide-react';

interface AuthModalProps {
  isOpen: boolean;
  initialMode?: 'login' | 'register';
  onClose: () => void;
  onSuccess: (user: AuthUser) => void;
}

const PROFESSIONS = [
  { id: 'farmer', label: 'Petani (Farmer)', desc: 'Spesialis bercocok tanam & panen komoditas ladang' },
  { id: 'rancher', label: 'Peternak (Rancher)', desc: 'Spesialis hewan ternak, susu, wol, & produk kandang' },
  { id: 'miner', label: 'Penambang (Miner)', desc: 'Spesialis bijih logam, permata, & eksplorasi gua' },
  { id: 'fisherman', label: 'Nelayan (Fisherman)', desc: 'Spesialis perikanan sungai, danau, & samudra' },
  { id: 'forager', label: 'Peramu Hutan (Forager)', desc: 'Spesialis kayu, hasil hutan liar, & sadapan pohon' },
];

export const AuthModal: React.FC<AuthModalProps> = ({
  isOpen,
  initialMode = 'login',
  onClose,
  onSuccess,
}) => {
  const [mode, setMode] = useState<'login' | 'register' | 'verify'>(initialMode);
  const [username, setUsername] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [profession, setProfession] = useState('farmer');
  const [verifyCode, setVerifyCode] = useState('');
  const [devCodeHint, setDevCodeHint] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [infoMessage, setInfoMessage] = useState<string | null>(null);

  useEffect(() => {
    if (isOpen) {
      setMode(initialMode);
      setError(null);
      setInfoMessage(null);
      setDevCodeHint(null);
    }
  }, [isOpen, initialMode]);

  if (!isOpen) return null;

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!username.trim() || !password) {
      setError('Harap masukkan username dan password.');
      return;
    }

    setLoading(true);
    setError(null);
    setInfoMessage(null);

    try {
      const res = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username: username.trim(), password }),
      });

      const data = await res.json().catch(() => ({}));

      if (!res.ok) {
        if (data.requiresVerification) {
          setEmail(data.email || '');
          setUsername(data.username || username.trim());
          setMode('verify');
          setInfoMessage(data.error || 'Masukkan kode verifikasi 6-digit untuk mengaktifkan akun.');
          return;
        }
        throw new Error(data.error || data.message || 'Username atau password salah.');
      }

      const roleNormalized = String(data.role || 'player').toLowerCase();
      const user: AuthUser = {
        token: data.token,
        role: roleNormalized === 'admin' || roleNormalized === 'host' ? 'admin' : 'player',
        username: data.username || username.trim(),
        email: data.email,
        userId: data.userId,
        profession: data.profession,
      };

      setStoredAuth(user);
      onSuccess(user);
      onClose();
    } catch (err: any) {
      setError(err.message || 'Gagal masuk. Periksa kembali kredensial Anda.');
    } finally {
      setLoading(false);
    }
  };

  const handleRegister = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!username.trim() || !password) {
      setError('Username dan password wajib diisi.');
      return;
    }
    if (password.length < 6) {
      setError('Password minimal terdiri dari 6 karakter.');
      return;
    }

    setLoading(true);
    setError(null);
    setInfoMessage(null);

    try {
      const res = await fetch('/api/auth/register', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          username: username.trim(),
          email: email.trim(),
          password,
          profession,
        }),
      });

      const data = await res.json().catch(() => ({}));

      if (!res.ok) {
        throw new Error(data.error || data.message || 'Gagal mendaftarkan akun baru.');
      }

      if (data.requiresVerification) {
        setMode('verify');
        setInfoMessage(data.message || 'Pendaftaran berhasil! Silakan masukkan kode verifikasi 6-digit.');
        if (data.devCode) {
          setDevCodeHint(data.devCode);
          setVerifyCode(data.devCode);
        }
        return;
      }

      const user: AuthUser = {
        token: data.token,
        role: 'player',
        username: data.username || username.trim(),
        email: data.email,
        userId: data.userId,
        profession: data.profession || profession,
      };

      setStoredAuth(user);
      onSuccess(user);
      onClose();
    } catch (err: any) {
      setError(err.message || 'Terjadi kesalahan saat pendaftaran.');
    } finally {
      setLoading(false);
    }
  };

  const handleVerifyEmail = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!verifyCode.trim()) {
      setError('Masukkan kode verifikasi 6-digit.');
      return;
    }

    setLoading(true);
    setError(null);

    try {
      const res = await fetch('/api/auth/verify-email', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          username: username.trim(),
          email: email.trim(),
          code: verifyCode.trim(),
        }),
      });

      const data = await res.json().catch(() => ({}));
      if (!res.ok) {
        throw new Error(data.error || 'Kode verifikasi salah atau kedaluwarsa.');
      }

      const user: AuthUser = {
        token: data.token,
        role: 'player',
        username: data.username || username.trim(),
        email: data.email,
        userId: data.userId,
        profession: data.profession || profession,
      };

      setStoredAuth(user);
      onSuccess(user);
      onClose();
    } catch (err: any) {
      setError(err.message || 'Verifikasi gagal.');
    } finally {
      setLoading(false);
    }
  };

  const handleResendCode = async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await fetch('/api/auth/resend-verification', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username: username.trim(), email: email.trim() }),
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data.error || 'Gagal mengirim ulang kode.');
      setInfoMessage(data.message || 'Kode verifikasi baru telah dikirim.');
      if (data.devCode) {
        setDevCodeHint(data.devCode);
        setVerifyCode(data.devCode);
      }
    } catch (err: any) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-950/75 backdrop-blur-sm transition-all">
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl w-full max-w-md overflow-hidden shadow-2xl transition-colors">
        {/* Header */}
        <div className="p-5 border-b border-slate-200 dark:border-slate-800 flex items-center justify-between">
          <div className="flex items-center space-x-2.5">
            <div className="p-2 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 rounded-lg border border-emerald-500/20">
              {mode === 'register' ? (
                <UserPlus className="w-4 h-4" />
              ) : mode === 'verify' ? (
                <ShieldCheck className="w-4 h-4" />
              ) : (
                <Lock className="w-4 h-4" />
              )}
            </div>
            <div>
              <h3 className="font-bold text-slate-900 dark:text-slate-100 text-sm">
                {mode === 'register'
                  ? 'Daftar Akun Petani Baru'
                  : mode === 'verify'
                  ? 'Verifikasi Kode Keamanan'
                  : 'Masuk Portal Server'}
              </h3>
              <p className="text-[11px] text-slate-500 dark:text-slate-400">
                {mode === 'register'
                  ? 'Pilih profesi awal & dapatkan kode OTP !auth untuk masuk ke game'
                  : mode === 'verify'
                  ? 'Konfirmasi kode 6-digit untuk mengaktifkan akun petani'
                  : 'Masuk sebagai Pemain (OTP !auth) atau Operator Sysadmin'}
              </p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="p-1 rounded-lg text-slate-400 hover:text-slate-800 dark:hover:text-white hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Mode Switcher Tabs */}
        {mode !== 'verify' && (
          <div className="grid grid-cols-2 border-b border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-950/50 p-1.5 gap-1.5">
            <button
              type="button"
              onClick={() => {
                setMode('login');
                setError(null);
              }}
              className={`flex items-center justify-center space-x-1.5 py-2 rounded-lg text-xs font-semibold transition-colors ${
                mode === 'login'
                  ? 'bg-white dark:bg-slate-800 text-emerald-600 dark:text-emerald-400 shadow-sm border border-slate-200/80 dark:border-slate-700'
                  : 'text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-200'
              }`}
            >
              <LogIn className="w-3.5 h-3.5" />
              <span>Masuk (Pemain / Admin)</span>
            </button>
            <button
              type="button"
              onClick={() => {
                setMode('register');
                setError(null);
              }}
              className={`flex items-center justify-center space-x-1.5 py-2 rounded-lg text-xs font-semibold transition-colors ${
                mode === 'register'
                  ? 'bg-white dark:bg-slate-800 text-emerald-600 dark:text-emerald-400 shadow-sm border border-slate-200/80 dark:border-slate-700'
                  : 'text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-200'
              }`}
            >
              <UserPlus className="w-3.5 h-3.5" />
              <span>Daftar Petani Baru</span>
            </button>
          </div>
        )}

        {/* Alerts */}
        <div className="px-5 pt-4 space-y-2">
          {error && (
            <div className="p-3 bg-rose-500/10 border border-rose-500/30 rounded-lg flex items-center space-x-2 text-rose-700 dark:text-rose-300 text-xs font-medium">
              <AlertCircle className="w-4 h-4 flex-shrink-0 text-rose-500 dark:text-rose-400" />
              <span>{error}</span>
            </div>
          )}
          {infoMessage && (
            <div className="p-3 bg-emerald-500/10 border border-emerald-500/30 rounded-lg flex items-center space-x-2 text-emerald-700 dark:text-emerald-300 text-xs font-medium">
              <CheckCircle2 className="w-4 h-4 flex-shrink-0 text-emerald-500 dark:text-emerald-400" />
              <span>{infoMessage}</span>
            </div>
          )}
        </div>

        {/* Form Login */}
        {mode === 'login' && (
          <form onSubmit={handleLogin} className="p-5 space-y-4">
            <div>
              <label className="block text-xs font-medium text-slate-700 dark:text-slate-300 mb-1.5">
                Username Pemain atau Admin
              </label>
              <div className="relative">
                <User className="w-4 h-4 text-slate-400 dark:text-slate-500 absolute left-3 top-2.5" />
                <input
                  type="text"
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                  placeholder="Contoh: PetaniJaya atau admin"
                  className="w-full bg-slate-50 dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded-lg pl-9 pr-3 py-2 text-xs text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-600 focus:outline-none focus:border-emerald-500"
                  required
                />
              </div>
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-700 dark:text-slate-300 mb-1.5">
                Password
              </label>
              <div className="relative">
                <KeyRound className="w-4 h-4 text-slate-400 dark:text-slate-500 absolute left-3 top-2.5" />
                <input
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="••••••••"
                  className="w-full bg-slate-50 dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded-lg pl-9 pr-3 py-2 text-xs text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-600 focus:outline-none focus:border-emerald-500"
                  required
                />
              </div>
              <p className="text-[10px] text-slate-500 mt-1">
                Untuk operator server, gunakan <code className="font-mono">ADMIN_USERNAME</code> &amp; <code className="font-mono">ADMIN_PASSWORD</code> dari <code className="font-mono">.env</code>.
              </p>
            </div>

            <div className="pt-1">
              <button
                type="submit"
                disabled={loading}
                className="w-full py-2.5 px-4 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white font-semibold text-xs transition-colors shadow flex items-center justify-center space-x-1.5"
              >
                <span>{loading ? 'Memverifikasi...' : 'Masuk ke Server'}</span>
              </button>
            </div>
          </form>
        )}

        {/* Form Register */}
        {mode === 'register' && (
          <form onSubmit={handleRegister} className="p-5 space-y-3.5">
            <div>
              <label className="block text-xs font-medium text-slate-700 dark:text-slate-300 mb-1">
                Username Petani
              </label>
              <div className="relative">
                <User className="w-4 h-4 text-slate-400 dark:text-slate-500 absolute left-3 top-2.5" />
                <input
                  type="text"
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                  placeholder="Nama akun unik Anda"
                  className="w-full bg-slate-50 dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded-lg pl-9 pr-3 py-2 text-xs text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-600 focus:outline-none focus:border-emerald-500"
                  required
                />
              </div>
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-700 dark:text-slate-300 mb-1">
                Alamat Email <span className="text-slate-400 font-normal">(Opsional di Self-Host LAN)</span>
              </label>
              <div className="relative">
                <Mail className="w-4 h-4 text-slate-400 dark:text-slate-500 absolute left-3 top-2.5" />
                <input
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="Kosongkan untuk aktivasi instan tanpa email"
                  className="w-full bg-slate-50 dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded-lg pl-9 pr-3 py-2 text-xs text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-600 focus:outline-none focus:border-emerald-500"
                />
              </div>
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-700 dark:text-slate-300 mb-1">
                Password Akun
              </label>
              <div className="relative">
                <KeyRound className="w-4 h-4 text-slate-400 dark:text-slate-500 absolute left-3 top-2.5" />
                <input
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder="Minimal 6 karakter"
                  className="w-full bg-slate-50 dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded-lg pl-9 pr-3 py-2 text-xs text-slate-900 dark:text-slate-100 placeholder-slate-400 dark:placeholder-slate-600 focus:outline-none focus:border-emerald-500"
                  required
                />
              </div>
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-700 dark:text-slate-300 mb-1 flex items-center space-x-1.5">
                <Briefcase className="w-3.5 h-3.5 text-emerald-500" />
                <span>Pilih Profesi Spesialisasi Awal</span>
              </label>
              <select
                value={profession}
                onChange={(e) => setProfession(e.target.value)}
                className="w-full bg-slate-50 dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded-lg px-3 py-2 text-xs text-slate-900 dark:text-slate-100 focus:outline-none focus:border-emerald-500"
              >
                {PROFESSIONS.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.label} — {p.desc}
                  </option>
                ))}
              </select>
            </div>

            <div className="pt-1">
              <button
                type="submit"
                disabled={loading}
                className="w-full py-2.5 px-4 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white font-semibold text-xs transition-colors shadow flex items-center justify-center space-x-1.5"
              >
                <span>{loading ? 'Mendaftarkan...' : 'Daftar & Ambil Kode OTP !auth'}</span>
              </button>
            </div>
          </form>
        )}

        {/* Form Verify Email */}
        {mode === 'verify' && (
          <form onSubmit={handleVerifyEmail} className="p-5 space-y-4">
            {devCodeHint && (
              <div className="p-3 bg-amber-500/10 border border-amber-500/30 rounded-lg text-xs text-amber-700 dark:text-amber-300">
                Mode Lokal (Tanpa Resend SMTP): Kode verifikasi Anda adalah{' '}
                <strong className="font-mono text-sm underline">{devCodeHint}</strong>
              </div>
            )}

            <div>
              <label className="block text-xs font-medium text-slate-700 dark:text-slate-300 mb-1.5">
                Kode Verifikasi 6-Digit
              </label>
              <input
                type="text"
                maxLength={6}
                value={verifyCode}
                onChange={(e) => setVerifyCode(e.target.value)}
                placeholder="123456"
                className="w-full bg-slate-50 dark:bg-slate-950 border border-slate-300 dark:border-slate-800 rounded-lg px-4 py-2.5 text-center font-mono text-base tracking-widest text-slate-900 dark:text-slate-100 focus:outline-none focus:border-emerald-500"
                required
              />
            </div>

            <div className="flex items-center justify-between gap-2">
              <button
                type="button"
                onClick={handleResendCode}
                disabled={loading}
                className="px-3 py-2 rounded-lg bg-slate-100 dark:bg-slate-800 hover:bg-slate-200 dark:hover:bg-slate-700 text-xs font-medium text-slate-700 dark:text-slate-300"
              >
                Kirim Ulang Kode
              </button>
              <button
                type="submit"
                disabled={loading}
                className="flex-1 py-2 px-4 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white font-semibold text-xs transition-colors shadow"
              >
                {loading ? 'Memverifikasi...' : 'Verifikasi & Masuk'}
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
};
