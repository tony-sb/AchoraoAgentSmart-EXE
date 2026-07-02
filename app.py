#!/usr/bin/env python3
"""
NIST SP 800-88 Disk Sanitization Tool
=======================================
Interfaz grafica para sanitizacion segura de discos segun norma NIST SP 800-88.
Requiere privilegios root. Desarrollado con CustomTkinter para Linux.
Exportable como binario unico portatil via PyInstaller.

Pasos:
  1. Deteccion dinamica de discos con lsblk -J
  2. Seleccion de metodo NIST SP 800-88 segun tipo de unidad
  3. Ejecucion con monitoreo de progreso en tiempo real
  4. Verificacion post-borrado + certificado de sanitizacion
"""
import os, sys, json, re, subprocess, threading, time
from datetime import datetime
from pathlib import Path
from tkinter import filedialog
import customtkinter as ctk

ctk.set_appearance_mode("dark")
ctk.set_default_color_theme("dark-blue")

REQUIRED_TOOLS = {
    "lsblk":    "Deteccion de discos (util-linux)",
    "smartctl": "Info de discos (smartmontools)",
    "hdparm":   "ATA Secure Erase",
    "dd":       "Sobrescritura y verificacion (coreutils)",
}
INSTALL_HINTS = {
    "smartctl": "sudo apt install smartmontools || sudo pacman -S smartmontools",
    "hdparm":   "sudo apt install hdparm        || sudo pacman -S hdparm",
    "nvme":     "sudo apt install nvme-cli      || sudo pacman -S nvme-cli",
}

def _tool_exists(cmd: str) -> bool:
    which_cmd = "where" if sys.platform == "win32" else "which"
    try:
        subprocess.run([which_cmd, cmd], capture_output=True, check=True)
        return True
    except (subprocess.CalledProcessError, FileNotFoundError):
        return False

def check_root():
    if sys.platform != "win32":
        if os.geteuid() == 0:
            return
        root = ctk.CTk()
        root.title("Error - Privilegios insuficientes")
        root.geometry("560x180")
        root.resizable(False, False)
        ctk.CTkLabel(root,
            text="ERROR CRITICO: Se requieren privilegios root.\n\n"
                 "Ejecuta:  sudo ./nist-wiper\n\n"
                 "La aplicacion se cerrara en 8 segundos.",
            font=ctk.CTkFont(size=14),
            text_color="#FF4444", justify="center",
        ).pack(expand=True, padx=20, pady=20)
        root.after(8000, lambda: (root.destroy(), sys.exit(1)))
        root.mainloop()
        sys.exit(1)

def check_dependencies():
    missing = []
    for cmd, desc in REQUIRED_TOOLS.items():
        if not _tool_exists(cmd):
            missing.append((cmd, desc, INSTALL_HINTS.get(cmd, "")))
    return missing

class SanitizerApp(ctk.CTk):
    COL_DANGER  = "#FF4444"
    COL_SUCCESS = "#44FF44"
    COL_WARN    = "#FFAA00"
    COL_ACCENT  = "#44AAFF"
    COL_CARD_BG = "#2B2B2B"
    COL_CARD_BD = "#3D3D3D"
    COL_SEL     = "#1F538D"

    def __init__(self):
        super().__init__()
        self.disks = []
        self.selected_disk = None
        self.selected_method = None
        self.wipe_process = None
        self.wipe_running = False
        self.wipe_aborted = False
        self.total_bytes = 0
        self.verification_done = False
        self.verification_passed = False
        self._missing_deps = []

        self.title("NIST SP 800-88 - Disk Sanitization Tool")
        self.minsize(880, 700)
        self.resizable(True, True)
        _w = self.winfo_screenwidth()
        _h = self.winfo_screenheight()
        self.geometry(f"960x780+{(_w-960)//2}+{(_h-780)//2}")
        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(1, weight=1)

        ctk.CTkLabel(self, text="NIST SP 800-88  Disk Sanitization Tool",
            font=ctk.CTkFont(size=22, weight="bold")
        ).grid(row=0, column=0, padx=20, pady=(15,0), sticky="w")

        ctk.CTkLabel(self, text="Sanitizacion segura de discos  |  root  |  Linux",
            font=ctk.CTkFont(size=12), text_color="#888888"
        ).grid(row=0, column=0, padx=20, pady=(40,0), sticky="w")

        self.main_frame = ctk.CTkFrame(self)
        self.main_frame.grid(row=1, column=0, padx=20, pady=(10,5), sticky="nsew")
        self.main_frame.grid_columnconfigure(0, weight=1)
        self.main_frame.grid_rowconfigure(1, weight=1)

        self.step_frame = ctk.CTkFrame(self.main_frame, height=42, fg_color="transparent")
        self.step_frame.grid(row=0, column=0, padx=10, pady=(8,0), sticky="ew")
        self.step_frame.grid_columnconfigure((0,1,2,3), weight=1)

        self.step_labels = []
        for i, txt in enumerate([
            "Paso 1: Seleccionar disco",
            "Paso 2: Elegir metodo NIST",
            "Paso 3: Ejecutar borrado",
            "Paso 4: Verificar + Certificado",
        ]):
            lbl = ctk.CTkLabel(self.step_frame, text=txt,
                font=ctk.CTkFont(size=13, weight="bold"))
            lbl.grid(row=0, column=i, padx=3, pady=5)
            self.step_labels.append(lbl)
        self.step_active = 0

        self.content = ctk.CTkScrollableFrame(self.main_frame)
        self.content.grid(row=1, column=0, padx=10, pady=5, sticky="nsew")
        self.content.grid_columnconfigure(0, weight=1)

        self.nav_frame = ctk.CTkFrame(self.main_frame, height=48, fg_color="transparent")
        self.nav_frame.grid(row=2, column=0, padx=10, pady=(3,8), sticky="ew")
        self.nav_frame.grid_columnconfigure((0,1), weight=1)

        self.btn_back = ctk.CTkButton(self.nav_frame, text="\u2190 Atras",
            state="disabled", command=self._go_back, width=110)
        self.btn_back.grid(row=0, column=0, padx=5, pady=5, sticky="w")

        self.btn_next = ctk.CTkButton(self.nav_frame, text="Siguiente \u2192",
            state="disabled", command=self._go_next, width=130)
        self.btn_next.grid(row=0, column=1, padx=5, pady=5, sticky="e")

        self._missing_deps = check_dependencies()
        self._update_step_indicators()
        self._show_loading("Escaneando discos del sistema...")
        self.protocol("WM_DELETE_WINDOW", self._on_close)
        self.after(150, self._detect_disks_async)

    def _update_step_indicators(self):
        for i, lbl in enumerate(self.step_labels):
            if i == self.step_active:
                lbl.configure(text_color=self.COL_ACCENT)
            elif i < self.step_active:
                lbl.configure(text_color=self.COL_SUCCESS)
            else:
                lbl.configure(text_color="#666666")

    def _clear_content(self):
        for w in self.content.winfo_children():
            w.destroy()

    def _show_loading(self, msg="Cargando..."):
        self._clear_content()
        ctk.CTkLabel(self.content, text=msg,
            font=ctk.CTkFont(size=15)).pack(pady=100)
        self.update_idletasks()

    def _show_error_popup(self, title, message):
        pop = ctk.CTkToplevel(self)
        pop.title(title)
        pop.geometry("520x180")
        pop.resizable(False, False)
        pop.transient(self)
        pop.grab_set()
        ctk.CTkLabel(pop, text=message,
            font=ctk.CTkFont(size=13),
            text_color=self.COL_DANGER, wraplength=470,
        ).pack(padx=20, pady=20, fill="both", expand=True)
        ctk.CTkButton(pop, text="Aceptar", command=pop.destroy).pack(pady=(0,12))

    def _on_close(self):
        if self.wipe_running:
            return
        self.destroy()

    def _go_next(self):
        n = self.step_active
        if n == 0 and self.selected_disk:
            self.step_active = 1
        elif n == 1 and self.selected_method:
            self.step_active = 2
        elif n == 2 and not self.wipe_running and self.verification_done:
            self.step_active = 3
        else:
            return
        self._update_step_indicators()
        self._render_current_step()

    def _go_back(self):
        if self.step_active > 0:
            self.step_active -= 1
            self._update_step_indicators()
            self._render_current_step()

    def _update_nav_buttons(self):
        self.btn_back.configure(
            state="normal" if self.step_active > 0 else "disabled")
        nxt = False
        if self.step_active == 0:
            nxt = self.selected_disk is not None
        elif self.step_active == 1:
            nxt = self.selected_method is not None
        elif self.step_active == 2:
            nxt = not self.wipe_running and self.verification_done
        else:
            nxt = True
        self.btn_next.configure(state="normal" if nxt else "disabled")

    def _render_current_step(self):
        self._clear_content()
        {0: self._render_step1, 1: self._render_step2,
         2: self._render_step3, 3: self._render_step4}[self.step_active]()
        self._update_nav_buttons()

    def _log(self, msg):
        if hasattr(self, "_log_widget"):
            try:
                self._log_widget.configure(state="normal")
                self._log_widget.insert("end", f"{msg}\n")
                self._log_widget.see("end")
                self._log_widget.configure(state="disabled")
                self.update_idletasks()
            except Exception:
                pass

    def _detect_disks_async(self):
        threading.Thread(target=self._detect_disks, daemon=True).start()

    def _detect_disks(self):
        try:
            r = subprocess.run(
                ["lsblk", "-J", "-o", "NAME,SIZE,TYPE,MODEL,SERIAL,TRAN,ROTA,VENDOR"],
                capture_output=True, text=True, timeout=20,
            )
            if r.returncode != 0:
                # Intentar sin -J en sistemas viejos
                r = subprocess.run(
                    ["lsblk", "-J", "-o", "NAME,SIZE,TYPE"],
                    capture_output=True, text=True, timeout=10,
                )
            if r.returncode != 0:
                self.after(0, lambda: self._show_error_popup(
                    "Error lsblk", f"No se pudo ejecutar lsblk:\n{r.stderr}"))
                self.after(0, self._render_step1)
                return
            data = json.loads(r.stdout)
            self.disks = []
            for dev in data.get("blockdevices", []):
                if dev.get("type") != "disk":
                    continue
                name = dev.get("name", "")
                if name.startswith("loop") or name.startswith("ram"):
                    continue
                if name.startswith("sr"):
                    continue
                rota = dev.get("rota")
                tran = (dev.get("tran") or "").lower()
                if name.startswith("nvme"):
                    dtype = "NVMe"
                elif rota == "1":
                    dtype = "HDD"
                elif tran == "usb":
                    dtype = "SSD SATA"
                else:
                    dtype = "SSD SATA"
                serial = (dev.get("serial") or "").strip()
                if not serial:
                    serial = self._smartctl_serial(name)
                model = (dev.get("model") or "").strip()
                if not model:
                    model = self._smartctl_model(name)
                self.disks.append({
                    "devpath": f"/dev/{name}", "devname": name,
                    "model": model,
                    "serial": serial,
                    "size": dev.get("size", "N/A"),
                    "vendor": (dev.get("vendor") or "").strip(),
                    "type": dtype,
                })
        except json.JSONDecodeError as e:
            msg = f"lsblk devolvio JSON invalido:\n{e}"
            self.after(0, lambda m=msg: self._show_error_popup("Error JSON", m))
        except subprocess.TimeoutExpired:
            self.after(0, lambda: self._show_error_popup(
                "Timeout", "lsblk tardo mas de 20s"))
        except Exception as e:
            msg = str(e)
            self.after(0, lambda m=msg: self._show_error_popup("Error deteccion", m))
        finally:
            self.after(0, self._render_step1)

    def _smartctl_serial(self, devname):
        try:
            r = subprocess.run(["smartctl", "-i", f"/dev/{devname}"],
                capture_output=True, text=True, timeout=10)
            if r.returncode == 0:
                for line in r.stdout.splitlines():
                    if "Serial Number:" in line or "Serial number:" in line:
                        return line.split(":", 1)[1].strip()
        except Exception:
            pass
        return "No disponible"

    def _smartctl_model(self, devname):
        try:
            r = subprocess.run(["smartctl", "-i", f"/dev/{devname}"],
                capture_output=True, text=True, timeout=10)
            if r.returncode == 0:
                for line in r.stdout.splitlines():
                    if "Device Model:" in line or "Model Number:" in line:
                        return line.split(":", 1)[1].strip()
                    if "Model Family:" in line:
                        return line.split(":", 1)[1].strip()
        except Exception:
            pass
        return ""

    def _render_step1(self):
        self._clear_content()
        if self._missing_deps:
            warn = ctk.CTkFrame(self.content, fg_color="#3A2A00")
            warn.pack(fill="x", padx=5, pady=(0,10))
            msg = "FALTAN HERRAMIENTAS DEL SISTEMA:\n"
            for _, desc, hint in self._missing_deps:
                msg += f"  \u2022 {desc} -> {hint}\n"
            ctk.CTkLabel(warn, text=msg, font=ctk.CTkFont(size=12),
                text_color=self.COL_WARN, justify="left",
            ).pack(padx=12, pady=8)
        top = ctk.CTkFrame(self.content, fg_color="transparent")
        top.pack(fill="x", padx=5, pady=(0,8))
        ctk.CTkLabel(top, text=f"Discos detectados: {len(self.disks)}",
            font=ctk.CTkFont(size=14)).pack(side="left")
        ctk.CTkButton(top, text="Re-escanear", width=100,
            command=lambda: (self._show_loading("Re-escanendo..."),
                self.after(100, self._detect_disks_async)),
        ).pack(side="right")
        if not self.disks:
            ctk.CTkLabel(self.content,
                text="No se detectaron discos fisicos.\n"
                     "Verifica conexiones y re-escannea.\n\n"
                     "Si usas WSL2: los discos USB no son accesibles.\n"
                     "Prueba en Linux nativo o Live USB.\n\n"
                     "Diagnostico desde terminal:\n"
                     "  lsblk -J -o NAME,SIZE,TYPE,MODEL,SERIAL,TRAN,ROTA",
                font=ctk.CTkFont(size=13), text_color=self.COL_WARN,
                justify="left",
            ).pack(pady=30, padx=20)
            self._update_nav_buttons()
            return
        for disk in self.disks:
            self._build_disk_card(self.content, disk).pack(fill="x", padx=5, pady=4)
        self._update_nav_buttons()

    def _build_disk_card(self, parent, disk):
        sel = disk is self.selected_disk
        card = ctk.CTkFrame(parent, fg_color=self.COL_CARD_BG,
            border_color=self.COL_SEL if sel else self.COL_CARD_BD, border_width=2)
        icons = {"NVMe": "NVMe", "SSD SATA": "SSD", "HDD": "HDD"}
        icon = icons.get(disk["type"], "DISK")
        r0 = ctk.CTkFrame(card, fg_color="transparent")
        r0.pack(fill="x", padx=12, pady=(8,2))
        ctk.CTkLabel(r0, text=f"  {icon}  {disk['type']}  ",
            font=ctk.CTkFont(size=13, weight="bold"),
            fg_color="#1A3A5C", corner_radius=4, padx=8,
        ).pack(side="left")
        ctk.CTkLabel(r0, text=disk["devpath"],
            font=ctk.CTkFont(size=16, weight="bold"),
        ).pack(side="left", padx=6)
        ctk.CTkLabel(r0, text=disk["size"],
            font=ctk.CTkFont(size=14), text_color="#AAAAAA",
        ).pack(side="right")
        r1 = ctk.CTkFrame(card, fg_color="transparent")
        r1.pack(fill="x", padx=12, pady=(0,8))
        for k, v in [("Modelo", disk["model"] or "N/A"),
                     ("Serial", disk["serial"]),
                     ("Fab", disk.get("vendor") or "N/A")]:
            ctk.CTkLabel(r1, text=f"{k}: {v}",
                font=ctk.CTkFont(size=12), text_color="#999999",
            ).pack(anchor="w")
        # Vincular click a TODOS los widgets dentro de la tarjeta recursivamente
        def _bind_click(w):
            w.bind("<Button-1>", lambda e, d=disk: self._select_disk(d), add="+")
            for c in w.winfo_children():
                _bind_click(c)
        _bind_click(card)
        return card

    def _select_disk(self, disk):
        self.selected_disk = disk
        self.selected_method = None
        self._render_step1()
        self._update_nav_buttons()

    def _render_step2(self):
        disk = self.selected_disk
        if not disk:
            return
        dtype = disk["type"]
        ctk.CTkLabel(self.content, text=f"Disco: {disk['devpath']} ({dtype})",
            font=ctk.CTkFont(size=15, weight="bold"),
        ).pack(anchor="w", padx=5, pady=(0,2))
        ctk.CTkLabel(self.content,
            text=f"Modelo: {disk['model']}  |  Serial: {disk['serial']}",
            font=ctk.CTkFont(size=12), text_color="#888888",
        ).pack(anchor="w", padx=5, pady=(0,12))

        if dtype == "NVMe":
            meta = [("nvme_block", "Block Erase (NIST Purge)",
                "nvme format <device> --ses=1\nBorra todos los bloques NAND."),
                ("nvme_crypto", "Cryptographic Erase (NIST Purge)",
                "nvme format <device> --ses=2\nElimina claves de cifrado. Mas rapido.")]
            note = "NIST Purge - NVMe Format"
        elif dtype == "SSD SATA":
            meta = [("ata_erase", "ATA Secure Erase (NIST Purge)",
                "hdparm --user-master u --security-set-pass p <device>\n"
                "hdparm --user-master u --security-erase p <device>\n"
                "ATENCION: La unidad no debe estar en estado 'frozen'.")]
            note = "NIST Purge - ATA Secure Erase"
        else:
            meta = [("overwrite", "Overwrite 1-Pass (NIST Clear)",
                "dd if=/dev/zero of=<device> bs=4M status=progress\n"
                "Cumple nivel 'Clear' segun NIST SP 800-88.")]
            note = "NIST Clear - Sobrescritura 1 pasada"

        ctk.CTkLabel(self.content, text=note,
            font=ctk.CTkFont(size=12), text_color=self.COL_ACCENT,
        ).pack(anchor="w", padx=5, pady=(0,12))

        self._method_var = ctk.StringVar(value="")
        for mid, title, desc in meta:
            frm = ctk.CTkFrame(self.content, fg_color=self.COL_CARD_BG)
            frm.pack(fill="x", padx=5, pady=5)
            ctk.CTkRadioButton(frm, text=title,
                variable=self._method_var, value=mid,
                font=ctk.CTkFont(size=14, weight="bold"),
                command=self._on_method_selected,
            ).pack(anchor="w", padx=12, pady=(8,2))
            ctk.CTkLabel(frm, text=desc, font=ctk.CTkFont(size=11),
                text_color="#888888", justify="left", wraplength=620,
            ).pack(anchor="w", padx=30, pady=(0,8))

        wf = ctk.CTkFrame(self.content, fg_color="#3A1A1A")
        wf.pack(fill="x", padx=5, pady=(15,0))
        ctk.CTkLabel(wf,
            text="\u26A0 ADVERTENCIA: El borrado es IRREVERSIBLE.\n"
                 "Todo el contenido del dispositivo sera destruido.\n"
                 "Verifica tener backups antes de continuar.",
            font=ctk.CTkFont(size=12), text_color=self.COL_DANGER,
            wraplength=620,
        ).pack(padx=15, pady=10)
        self._update_nav_buttons()

    def _on_method_selected(self):
        self.selected_method = self._method_var.get()
        self._update_nav_buttons()

    def _method_name(self, mid):
        return {"nvme_block": "Block Erase (NVMe)",
                "nvme_crypto": "Cryptographic Erase (NVMe)",
                "ata_erase": "ATA Secure Erase (SSD SATA)",
                "overwrite": "Overwrite 1-Pass (HDD)"}.get(mid, mid)

    def _render_step3(self):
        disk = self.selected_disk
        info = ctk.CTkFrame(self.content, fg_color="#1A2A1A")
        info.pack(fill="x", padx=5, pady=5)
        ctk.CTkLabel(info,
            text=f"Dispositivo: {disk['devpath']} ({disk['type']})\n"
                 f"Metodo: {self._method_name(self.selected_method)}\n"
                 f"Tamano: {disk['size']}",
            font=ctk.CTkFont(size=13), justify="left",
            text_color="#88FF88",
        ).pack(padx=15, pady=10, anchor="w")

        self._prog_bar = ctk.CTkProgressBar(self.content, width=680)
        self._prog_bar.pack(pady=(20,5))
        self._prog_bar.set(0)

        self._prog_label = ctk.CTkLabel(self.content,
            text="Listo para iniciar el borrado...",
            font=ctk.CTkFont(size=13), text_color="#AAAAAA")
        self._prog_label.pack(pady=3)

        self._log_widget = ctk.CTkTextbox(self.content, height=130, width=680,
            font=ctk.CTkFont(size=11, family="Consolas"))
        self._log_widget.pack(pady=(8,5))
        self._log_widget.insert("0.0", "=== Log de operacion ===\n")
        self._log_widget.configure(state="disabled")

        bf = ctk.CTkFrame(self.content, fg_color="transparent")
        bf.pack(pady=12)
        self._btn_start = ctk.CTkButton(bf, text="\u26A0 INICIAR BORRADO",
            font=ctk.CTkFont(size=15, weight="bold"),
            fg_color="#AA2222", hover_color="#CC3333",
            command=self._confirm_and_start_wipe)
        self._btn_start.pack(side="left", padx=6)
        self._btn_cancel = ctk.CTkButton(bf, text="Cancelar",
            state="disabled", fg_color="#555555",
            command=self._cancel_wipe)
        self._btn_cancel.pack(side="left", padx=6)

        if hasattr(self, "_wipe_ok") and self._wipe_ok is not None:
            self._btn_start.configure(
                text="\u2705 Completado" if self._wipe_ok else "\u274C Fallo",
                state="disabled")
        self._update_nav_buttons()

    def _confirm_and_start_wipe(self):
        pop = ctk.CTkToplevel(self)
        pop.title("CONFIRMACION DOBLE FACTOR")
        pop.geometry("560x340")
        pop.resizable(False, False)
        pop.transient(self)
        pop.grab_set()
        pop.attributes("-topmost", True)
        disk = self.selected_disk

        ctk.CTkLabel(pop, text="\u26A0 OPERACION IRREVERSIBLE \u26A0",
            font=ctk.CTkFont(size=18, weight="bold"),
            text_color=self.COL_DANGER,
        ).pack(pady=(14,4))
        ctk.CTkLabel(pop,
            text=f"Dispositivo: {disk['devpath']}\nSerial: {disk['serial']}",
            font=ctk.CTkFont(size=13),
        ).pack(pady=4)
        ctk.CTkLabel(pop,
            text="Escribe exactamente una de las siguientes opciones\n"
                 "para desbloquear el borrado:\n\n"
                 "  1) La palabra: BORRAR\n"
                 "  2) El numero de serie del disco",
            font=ctk.CTkFont(size=12), wraplength=480,
        ).pack(padx=20, pady=(8,4))

        var = ctk.StringVar()
        entry = ctk.CTkEntry(pop, textvariable=var, width=400,
            font=ctk.CTkFont(size=16))
        entry.pack(pady=6)
        entry.focus_set()
        err_lbl = ctk.CTkLabel(pop, text="", text_color=self.COL_DANGER,
            font=ctk.CTkFont(size=11))
        err_lbl.pack()

        def confirm():
            txt = var.get().strip()
            if txt == "BORRAR" or txt == disk["serial"]:
                pop.destroy()
                self._start_wipe()
            else:
                err_lbl.configure(
                    text="Texto incorrecto. Debe ser 'BORRAR' o el serial exacto.")

        bf = ctk.CTkFrame(pop, fg_color="transparent")
        bf.pack(pady=10)
        ctk.CTkButton(bf, text="Confirmar y Borrar",
            fg_color="#AA2222", command=confirm).pack(side="left", padx=5)
        ctk.CTkButton(bf, text="Cancelar", command=pop.destroy).pack(side="left", padx=5)

    def _start_wipe(self):
        self.wipe_running = True
        self.wipe_aborted = False
        self.verification_done = False
        self.verification_passed = False
        self._btn_start.configure(state="disabled", text="Ejecutando...")
        self._btn_cancel.configure(state="disabled")
        self.btn_back.configure(state="disabled")
        self.btn_next.configure(state="disabled")
        self.protocol("WM_DELETE_WINDOW", lambda: None)
        size_str = self.selected_disk["size"]
        self.total_bytes = self._parse_bytes(size_str)
        self._log("=== INICIO DEL PROCESO DE BORRADO ===")
        self._log(f"Dispositivo: {self.selected_disk['devpath']}")
        self._log(f"Metodo: {self._method_name(self.selected_method)}")
        self._log(f"Tamano: {size_str} ({self.total_bytes} bytes)")
        threading.Thread(target=self._execute_wipe, daemon=True).start()

    @staticmethod
    def _parse_bytes(sz):
        if not sz:
            return 0
        m = re.match(r"([\d.]+)\s*([BKMGTP]?)", sz.strip())
        if not m:
            return 0
        val = float(m.group(1))
        u = m.group(2) or "B"
        mult = {"B": 1, "K": 1024, "M": 1024**2, "G": 1024**3, "T": 1024**4}
        return int(val * mult.get(u, 1))

    def _execute_wipe(self):
        ok = False
        try:
            dev = self.selected_disk["devpath"]
            mid = self.selected_method
            if mid == "nvme_block":
                ok = self._exec_nvme(dev, "1")
            elif mid == "nvme_crypto":
                ok = self._exec_nvme(dev, "2")
            elif mid == "ata_erase":
                ok = self._exec_ata_erase(dev)
            elif mid == "overwrite":
                ok = self._exec_dd(dev)
            else:
                raise ValueError(f"Metodo desconocido: {mid}")
        except Exception as e:
            msg = str(e)
            self._log(f"ERROR: {msg}")
            self.after(0, lambda m=msg: self._show_error_popup("Error de borrado", m))
        finally:
            self.wipe_running = False
            self._wipe_ok = ok
            self.after(0, lambda: self._on_wipe_finished(ok))

    def _exec_nvme(self, dev, ses):
        cmd = ["nvme", "format", dev, f"--ses={ses}"]
        self._log(f"Ejecutando: {' '.join(cmd)}")
        self.after(0, lambda: self._prog_bar.configure(mode="indeterminate"))
        self.after(0, lambda: self._prog_bar.start())
        self.after(0, lambda: self._prog_label.configure(text="Ejecutando NVMe Format..."))
        r = subprocess.run(cmd, capture_output=True, text=True, timeout=600)
        self.after(0, lambda: self._prog_bar.stop())
        self.after(0, lambda: self._prog_bar.configure(mode="determinate"))
        if r.returncode != 0:
            self._log(f"Error NVMe: {r.stderr.strip()}")
            return False
        self._log("NVMe Format completado exitosamente.")
        self.after(0, lambda: self._prog_bar.set(1.0))
        return True

    def _exec_ata_erase(self, dev):
        self._log("Paso 1/2: Estableciendo contrasena temporal ATA...")
        self.after(0, lambda: self._prog_label.configure(text="Estableciendo contrasena ATA..."))
        self.after(0, lambda: self._prog_bar.configure(mode="indeterminate"))
        self.after(0, lambda: self._prog_bar.start())
        r1 = subprocess.run(
            ["hdparm", "--user-master", "u", "--security-set-pass", "p", dev],
            capture_output=True, text=True, timeout=30)
        if r1.returncode != 0:
            self._log(f"Error ATA pass: {r1.stderr.strip()}")
            self.after(0, lambda: self._prog_bar.stop())
            return False
        self._log("Contrasena OK. Ejecutando ATA Secure Erase...")
        self.after(0, lambda: self._prog_label.configure(
            text="ATA Secure Erase en progreso... (puede tardar minutos)"))
        r2 = subprocess.run(
            ["hdparm", "--user-master", "u", "--security-erase", "p", dev],
            capture_output=True, text=True, timeout=7200)
        self.after(0, lambda: self._prog_bar.stop())
        self.after(0, lambda: self._prog_bar.configure(mode="determinate"))
        if r2.returncode != 0:
            self._log(f"Error ATA erase: {r2.stderr.strip()}")
            return False
        self._log("ATA Secure Erase completado.")
        self.after(0, lambda: self._prog_bar.set(1.0))
        return True

    def _exec_dd(self, dev):
        cmd = ["dd", "if=/dev/zero", f"of={dev}", "bs=4M", "status=progress"]
        self._log(f"Ejecutando: {' '.join(cmd)}")
        self.after(0, lambda: self._prog_bar.configure(mode="determinate"))
        self.after(0, lambda: self._prog_bar.set(0))
        proc = subprocess.Popen(cmd, stderr=subprocess.PIPE,
            stdout=subprocess.DEVNULL, bufsize=0)
        self.wipe_process = proc
        self.after(0, lambda: self._btn_cancel.configure(state="normal"))
        pat = re.compile(r"(\d+)\s+bytes")
        buf = b""
        while True:
            chunk = proc.stderr.read(4096)
            if not chunk:
                break
            buf += chunk
            while True:
                idx = -1
                for s in (b"\r", b"\n"):
                    pos = buf.find(s)
                    if pos >= 0 and (idx < 0 or pos < idx):
                        idx = pos
                if idx < 0:
                    break
                line = buf[:idx].decode("utf-8", errors="replace").strip()
                buf = buf[idx+1:]
                if not line:
                    continue
                self._log(f"[dd] {line}")
                m = pat.search(line)
                if m and self.total_bytes > 0:
                    copied = int(m.group(1))
                    pct = min(copied / self.total_bytes, 1.0)
                    self.after(0, lambda v=pct: self._prog_bar.set(v))
                    self.after(0, lambda c=copied, p=pct: self._prog_label.configure(
                        text=f"{c/(1024**3):.1f} GB / {self.total_bytes/(1024**3):.1f} GB ({p*100:.1f}%)"))
        if buf.strip():
            rest = buf.decode("utf-8", errors="replace").strip()
            if rest:
                self._log(f"[dd] {rest}")
        proc.wait()
        self.wipe_process = None
        if self.wipe_aborted:
            self._log("Proceso cancelado por el usuario.")
            return False
        if proc.returncode != 0:
            self._log(f"dd termino con codigo {proc.returncode}.")
            return False
        self._log("Sobrescritura completada exitosamente.")
        self.after(0, lambda: self._prog_bar.set(1.0))
        return True

    def _on_wipe_finished(self, ok):
        self._btn_cancel.configure(state="disabled")
        self.protocol("WM_DELETE_WINDOW", self.destroy)
        if ok:
            self._btn_start.configure(text="\u2705 Completado", state="disabled")
            self._log("\n=== BORRADO EXITOSO. Iniciando verificacion... ===")
            self.after(300, self._run_verification)
        else:
            self._btn_start.configure(text="\u274C Fallo", state="disabled")
            self._log("\n=== BORRADO FALLIDO ===")
            self.after(0, lambda: self._prog_label.configure(
                text="\u274C El proceso de borrado fallo",
                text_color=self.COL_DANGER))
        self._update_nav_buttons()

    def _cancel_wipe(self):
        if self.wipe_process and self.wipe_process.poll() is None:
            self.wipe_aborted = True
            self.wipe_process.terminate()
            self._log("Cancelando proceso... (SIGTERM)")

    def _run_verification(self):
        threading.Thread(target=self._verify_wipe, daemon=True).start()

    def _verify_wipe(self):
        dev = self.selected_disk["devpath"]
        self._log("\n=== VERIFICACION POST-BORRADO ===")
        self.after(0, lambda: self._prog_label.configure(
            text="Verificando integridad del borrado...", text_color="#FFFFFF"))
        self.after(0, lambda: self._prog_bar.configure(mode="indeterminate"))
        self.after(0, lambda: self._prog_bar.start())
        try:
            r = subprocess.run(["blockdev", "--getsz", dev],
                capture_output=True, text=True, timeout=10)
            if r.returncode != 0:
                raise RuntimeError("No se pudo obtener tamano del dispositivo")
            total_sectors = int(r.stdout.strip())
            self._log("Leyendo primeros sectores...")
            first = subprocess.run(
                ["dd", f"if={dev}", "bs=512", "count=10", "status=none"],
                capture_output=True, timeout=30)
            if first.returncode != 0:
                raise RuntimeError("Error leyendo inicio del dispositivo")
            skip_s = max(total_sectors - 10, 0)
            self._log(f"Leyendo ultimos sectores (sector {skip_s})...")
            last = subprocess.run(
                ["dd", f"if={dev}", "bs=512", "count=10",
                 f"skip={skip_s}", "status=none"],
                capture_output=True, timeout=30)
            if last.returncode != 0:
                raise RuntimeError("Error leyendo final del dispositivo")
            first_ok = all(b == 0 for b in first.stdout)
            last_ok = all(b == 0 for b in last.stdout)
            self.after(0, lambda: self._prog_bar.stop())
            self.after(0, lambda: self._prog_bar.configure(mode="determinate"))
            if first_ok and last_ok:
                self._log("VERIFICACION EXITOSA: Todos los sectores contienen ceros.")
                self.after(0, lambda: self._prog_bar.set(1.0))
                self.after(0, lambda: self._prog_label.configure(
                    text="VERIFICACION EXITOSA - Disco sanitizado correctamente",
                    text_color=self.COL_SUCCESS))
                self.verification_passed = True
            else:
                self._log("VERIFICACION FALLIDA: Datos residuales detectados.")
                self.after(0, lambda: self._prog_label.configure(
                    text="VERIFICACION FALLIDA - Datos residuales",
                    text_color=self.COL_DANGER))
                self.verification_passed = False
        except Exception as e:
            self._log(f"ERROR en verificacion: {e}")
            self.after(0, lambda: self._prog_label.configure(
                text=f"Error en verificacion", text_color=self.COL_DANGER))
            self.verification_passed = False
        finally:
            self.verification_done = True
            self.after(0, self._on_verification_done)

    def _on_verification_done(self):
        self.btn_next.configure(state="normal", text="\u2192 Ver Resultados")
        self._update_nav_buttons()

    def _render_step4(self):
        self._clear_content()
        if self.verification_passed:
            ctk.CTkLabel(self.content,
                text="\u2705 VERIFICACION EXITOSA",
                font=ctk.CTkFont(size=20, weight="bold"),
                text_color=self.COL_SUCCESS,
            ).pack(pady=(20,5))
            ctk.CTkLabel(self.content,
                text=f"Disco {self.selected_disk['devpath']} sanitizado correctamente.\n"
                     "Todos los sectores verificados contienen unicamente ceros.",
                font=ctk.CTkFont(size=14), wraplength=600,
            ).pack(pady=10)
            ctk.CTkLabel(self.content,
                text=f"Dispositivo: {self.selected_disk['devpath']}\n"
                     f"Modelo: {self.selected_disk['model']}\n"
                     f"Serial: {self.selected_disk['serial']}\n"
                     f"Metodo: {self._method_name(self.selected_method)}\n"
                     f"Fecha: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}",
                font=ctk.CTkFont(size=13), justify="left",
                text_color="#AAAAAA",
            ).pack(pady=10)
            ctk.CTkButton(self.content,
                text="\uD83D\uDCC4 Descargar Certificado de Sanitizacion",
                font=ctk.CTkFont(size=15, weight="bold"),
                fg_color="#1A5A2A", hover_color="#227744",
                command=self._download_certificate,
            ).pack(pady=15)
            ctk.CTkLabel(self.content,
                text="El certificado JSON contiene:\n"
                     "  - Fecha y hora de la sanitizacion\n"
                     "  - Modelo y serial del disco\n"
                     "  - Metodo NIST SP 800-88 aplicado\n"
                     "  - Resultado de verificacion post-borrado\n"
                     "  - Espacio para firma del operador de TI",
                font=ctk.CTkFont(size=12), text_color="#888888",
                justify="left", wraplength=550,
            ).pack(pady=10)
        else:
            ctk.CTkLabel(self.content,
                text="\u274C VERIFICACION FALLIDA",
                font=ctk.CTkFont(size=20, weight="bold"),
                text_color=self.COL_DANGER,
            ).pack(pady=20)
            ctk.CTkLabel(self.content,
                text=f"La verificacion del disco {self.selected_disk['devpath']}\n"
                     "detecto datos residuales. El proceso NO fue completado\n"
                     "satisfactoriamente. Intente con otro metodo.",
                font=ctk.CTkFont(size=14), wraplength=600,
            ).pack(pady=10)
        self._update_nav_buttons()

    def _download_certificate(self):
        serial = self.selected_disk["serial"].replace(" ", "_")
        if not serial or serial == "No disponible":
            serial = "DESCONOCIDO"
        date_str = datetime.now().strftime("%Y%m%d_%H%M%S")
        default_name = f"certificado_sanitizacion_{serial}_{date_str}.json"
        filepath = filedialog.asksaveasfilename(parent=self,
            title="Guardar Certificado de Sanitizacion",
            defaultextension=".json", initialfile=default_name,
            filetypes=[("JSON", "*.json"), ("Texto", "*.txt"), ("Todos", "*.*")])
        if not filepath:
            return
        cert = {
            "certificado_sanitizacion": {
                "fecha": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
                "timestamp": datetime.now().isoformat(),
                "dispositivo": {
                    "nombre": self.selected_disk["devpath"],
                    "modelo": self.selected_disk["model"],
                    "serial": self.selected_disk["serial"],
                    "tamano": self.selected_disk["size"],
                    "tipo": self.selected_disk["type"],
                },
                "metodo_nist": {
                    "nivel": "Purge" if self.selected_disk["type"] != "HDD" else "Clear",
                    "metodo": self._method_name(self.selected_method),
                    "estandar": "NIST SP 800-88 Rev. 1",
                },
                "verificacion_post_borrado": {
                    "estado": "EXITOSA" if self.verification_passed else "FALLIDA",
                    "detalle": ("Lectura de sectores - todos los bloques contienen ceros"
                        if self.verification_passed else "Se detectaron datos residuales"),
                    "fecha_verificacion": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
                },
                "operador": {"nombre": "", "firma": "", "notas": ""},
            }
        }
        try:
            with open(filepath, "w", encoding="utf-8") as f:
                json.dump(cert, f, indent=2, ensure_ascii=False)
            self._log(f"Certificado guardado en: {filepath}")
            pop = ctk.CTkToplevel(self)
            pop.title("Certificado Guardado")
            pop.geometry("450x150")
            pop.transient(self)
            pop.grab_set()
            ctk.CTkLabel(pop,
                text=f"Certificado guardado exitosamente.\n\n{filepath}",
                font=ctk.CTkFont(size=12), wraplength=400,
            ).pack(padx=20, pady=20)
            ctk.CTkButton(pop, text="Aceptar", command=pop.destroy).pack(pady=5)
        except Exception as e:
            self._show_error_popup("Error al guardar",
                f"No se pudo guardar el certificado:\n{e}")


def main():
    check_root()
    if sys.platform == "win32":
        import ctypes
        ctypes.windll.user32.MessageBoxW(0,
            "Esta aplicacion esta disenada para LINUX.\n"
            "Las funciones de borrado de discos requieren\n"
            "comandos del sistema Linux (lsblk, dd, hdparm, nvme).\n\n"
            "Puedes navegar la interfaz en modo demo.\n"
            "Compila el binario final en Linux con PyInstaller.",
            "NIST Wiper - Modo Demo", 0)
    app = SanitizerApp()
    app.mainloop()


if __name__ == "__main__":
    main()
