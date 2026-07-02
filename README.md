# NIST SP 800-88 Disk Sanitization Tool

Herramienta grafica para sanitizacion segura de discos segun norma **NIST SP 800-88 Rev. 1**.
Disenada para operadores de TI que necesitan borrar discos de forma irreversible y certificada.

---

## Requisitos del sistema

- Cualquier PC con **procesador x86-64** (Intel/AMD)
- **1 GB de RAM** minimo
- **Puerto USB** para bootear (si usas Live USB)
- **Disco a sanitizar** (HDD, SSD SATA o NVMe)

---

## Modo 1: Live USB (recomendado - sin instalacion)

Booteas desde un USB, ejecutas la herramienta, sanitizas los discos, apagas y todo vuelve a la normalidad.

### Paso 1 - Descargar una ISO Linux

| Distribucion | Tamano | Descarga |
|---|---|---|
| **SystemRescue** (~800 MB) | Incluye `nvme-cli`, `hdparm`, `smartctl` | [system-rescue.org](https://www.system-rescue.org/Download/) |
| **Ubuntu Desktop Live** (~5 GB) | Interface familiar | [ubuntu.com/download/desktop](https://ubuntu.com/download/desktop) |
| **Debian Live Xfce** (~3 GB) | Ligera y estable | [debian.org/CD/live](https://www.debian.org/CD/live/) |

SystemRescue es la mas practica porque ya trae todas las herramientas de disco preinstaladas.

### Paso 2 - Crear el USB booteable

**En Windows - con Rufus (gratuito):**

1. Descarga **Rufus** desde [rufus.ie](https://rufus.ie)
2. Abre Rufus, selecciona tu USB
3. En "Seleccion de arranque", elige la ISO descargada
4. Haz clic en **"Empezar"** y espera a que termine
5. Cierra Rufus y no retires el USB

**En Linux - con dd:**

```bash
sudo dd if=debian-live.iso of=/dev/sdX bs=4M status=progress
# (donde /dev/sdX es tu USB - cuidado de no equivocarte)
```

### Paso 3 - Copiar nist-wiper al USB

Una vez creado el USB booteable, copia el archivo `nist-wiper` a la raiz del USB.
En Windows, solo arrastra el archivo a la unidad USB en el Explorador.

### Paso 4 - Bootear desde el USB

1. Reinicia la PC
2. Durante el arranque presiona **F12**, **F9**, **Esc** o **Supr** (depende del fabricante) para entrar al menu de boot
3. Selecciona tu USB como dispositivo de arranque
4. Espera a que cargue el escritorio live

### Paso 5 - Instalar dependencias (solo si faltan)

Abre una terminal (`Ctrl + Alt + T` en la mayoria de los live) y ejecuta:

```bash
# Si usas SystemRescue - ya tiene todo instalado, salta este paso

# Si usas Ubuntu/Debian Live:
sudo apt update
sudo apt install -y nvme-cli hdparm smartmontools

# Si usas Arch Linux Live:
sudo pacman -S nvme-cli hdparm smartmontools
```

### Paso 6 - Ejecutar la herramienta

Desde la terminal:

```bash
cd /media/usb     # o la ruta donde monto el USB
ls                # para verificar que nist-wiper esta ahi
sudo ./nist-wiper
```

La interfaz grafica se abrira. Sigue los 4 pasos:

1. **Selecciona el disco** de la lista
2. **Elige el metodo NIST** segun el tipo de disco
3. **Confirma el borrado** escribiendo "BORRAR" o el serial exacto
4. **Descarga el certificado** al finalizar

---

## Modo 2: Instalacion en Linux (si tienes Linux instalado)

Si ya tienes Linux instalado (Ubuntu, Debian, Fedora, etc.):

```bash
# 1. Copia nist-wiper a cualquier carpeta
cp nist-wiper ~/

# 2. Instala las herramientas de disco (si faltan)
sudo apt install nvme-cli hdparm smartmontools

# 3. Ejecuta
sudo ./nist-wiper
```

No necesita Python ni nada mas - es un binario unico auto-contenido.

---

## Modo 3: Compilar desde codigo fuente (solo para desarrolladores)

Si quieres modificar el codigo o compilar tu propia version.

### En Linux (nativo o VM)

```bash
sudo apt update && sudo apt install -y python3 python3-pip python3-venv python3-tk
python3 -m venv venv
source venv/bin/activate
pip install customtkinter pyinstaller
pyinstaller --onefile --windowed --name nist-wiper app.py
# El binario queda en: dist/nist-wiper
```

### En Windows con WSL2 (entorno de desarrollo)

Este flujo te permite compilar y probar la herramienta desde Windows usando el subsistema Linux (WSL2) con interfaz grafica proyectada en tu escritorio Windows.

#### Requisitos previos en Windows

Para que la interfaz grafica de Linux funcione y se proyecte en tu pantalla de Windows, es obligatorio instalar un servidor X local.

Abre **PowerShell como Administrador** e instala el servidor grafico **VcXsrv**:

```powershell
winget install marha.VcXsrv
```

1. Ejecuta la aplicacion **XLaunch** desde el menu inicio de Windows
2. Avanza las opciones de configuracion y **marca obligatoriamente**:
   - **"Disable access control"** (Si no se marca, se denegara la conexion con WSL2)
3. Presiona **Finish** y mantén la aplicacion corriendo en segundo plano

#### Guia de Instalacion y Compilacion en WSL2

Ejecuta los siguientes bloques directamente desde tu **consola de PowerShell** de Windows:

```powershell
# ====== PASO 1: Instalar WSL2 (Una sola vez) ======
wsl --install -d Ubuntu-24.04

# ====== PASO 2: Verificar instalacion ======
wsl -d Ubuntu-24.04 whoami

# ====== PASO 3: Mover el proyecto a Ubuntu ======
cd C:\Z_Proyectos\AchoraoAgent
cat app.py | wsl -d Ubuntu-24.04 -u root -- bash -c 'cat > /root/app.py'

# ====== PASO 4: Preparar entorno e instalar dependencias de borrado ======
wsl -d Ubuntu-24.04 -u root -- bash -c '
  apt update
  apt install -y python3 python3-pip python3-venv python3-tk smartmontools hdparm nvme-cli
  python3 -m venv /root/venv
  /root/venv/bin/pip install customtkinter pyinstaller
'

# ====== PASO 5: Compilar el Binario Unico Linux ======
wsl -d Ubuntu-24.04 -u root -- bash -c '
  cd /root
  /root/venv/bin/pyinstaller --onefile --windowed --name nist-wiper app.py
'

# ====== PASO 6: Copiar el Binario de vuelta a Windows ======
wsl -d Ubuntu-24.04 -u root -- cp /root/dist/nist-wiper /mnt/c/Z_Proyectos/AchoraoAgent/
```

#### Ejecucion Interactiva Real (Grafica)

Cada vez que desees probar o ejecutar la aplicacion con permisos nativos de hardware en tu entorno virtual, hazlo desde tu **terminal de Ubuntu** (WSL):

```bash
# Extrae la direccion IP del puente de Windows
export DISPLAY=$(ip route | awk '/^default/{print $3}'):0

# Asigna permisos de ejecucion al binario (si no los tiene)
chmod +x ./nist-wiper

# Ejecuta la interfaz grafica interactiva heredando el servidor de video
sudo DISPLAY=$DISPLAY ./nist-wiper
```

#### Recompilar despues de cambios en el Codigo

Si modificas el codigo fuente `app.py` en tu carpeta local de Windows, corre este bloque de una sola vez en **PowerShell** para refrescar el binario:

```powershell
cat app.py | wsl -d Ubuntu-24.04 -u root -- bash -c 'cat > /root/app.py'
wsl -d Ubuntu-24.04 -u root -- bash -c 'cd /root && rm -rf build dist *.spec && /root/venv/bin/pyinstaller --onefile --windowed --name nist-wiper app.py'
wsl -d Ubuntu-24.04 -u root -- cp /root/dist/nist-wiper /mnt/c/Z_Proyectos/AchoraoAgent/
```

---

## Metodos de borrado soportados

| Tipo de disco | Metodo NIST | Comando real |
|---|---|---|
| NVMe | Block Erase (NIST Purge) | `nvme format --ses=1` |
| NVMe | Cryptographic Erase (NIST Purge) | `nvme format --ses=2` |
| SSD SATA | ATA Secure Erase (NIST Purge) | `hdparm --security-erase` |
| HDD | Overwrite 1-Pass (NIST Clear) | `dd if=/dev/zero` |

---

## Seguridad

- **Root obligatorio**: la app no arranca sin `sudo`
- **Doble confirmacion**: hay que escribir "BORRAR" o el serial exacto del disco
- **Sin cancelacion durante el borrado**: se bloquea el boton Cancel y el cierre de ventana
- **Verificacion post-borrado**: lee sectores al inicio y final del disco para confirmar que estan en cero
- **Certificado de sanitizacion**: exporta un JSON con fecha, metodo, serial y resultado

---

## Certificado de sanitizacion

Al finalizar, puedes descargar un archivo JSON con esta estructura:

```json
{
  "certificado_sanitizacion": {
    "fecha": "2026-07-01 17:30:00",
    "dispositivo": {
      "nombre": "/dev/sda",
      "modelo": "ST1000DM003-1CH162",
      "serial": "Z1D12345",
      "tamano": "931.5G",
      "tipo": "HDD"
    },
    "metodo_nist": {
      "nivel": "Clear",
      "metodo": "Overwrite 1-Pass (HDD)",
      "estandar": "NIST SP 800-88 Rev. 1"
    },
    "verificacion_post_borrado": {
      "estado": "EXITOSA",
      "detalle": "Lectura de sectores - todos los bloques contienen ceros"
    },
    "operador": {
      "nombre": "",
      "firma": "",
      "notas": ""
    }
  }
}
```

Completa los campos `operador` con el nombre y firma del tecnico responsable.

---

## Solucion de advertencias internas

Como se aprecia en la cabecera amarilla de la aplicacion, el sistema te alertara si el entorno no dispone de los comandos nativos para procesar el borrado. Corrigelo con:

```bash
sudo apt install -y smartmontools hdparm nvme-cli
```

---

## Solucion de problemas

| Problema | Causa | Solucion |
|---|---|---|
| `sudo: ./nist-wiper: command not found` | El archivo no esta en la carpeta actual | Usa `ls` para ver donde esta y `cd` a esa carpeta |
| `nvme: command not found` | Falta `nvme-cli` | `sudo apt install nvme-cli` / `sudo pacman -S nvme-cli` |
| `hdparm: command not found` | Falta `hdparm` | `sudo apt install hdparm` / `sudo pacman -S hdparm` |
| `smartctl: command not found` | Falta `smartmontools` | `sudo apt install smartmontools` / `sudo pacman -S smartmontools` |
| `_tkinter.TclError: no display name` | Sin servidor X | Usa VcXsrv o ejecuta en Linux con monitor |
| La app no arranca (sin error) | No tienes permisos de ejecucion | `sudo chmod +x nist-wiper` |
| "Disk is frozen" en ATA Secure Erase | La BIOS congelo el disco | Suspende/reanuda la PC o usa otro metodo |

---

## Licencia

Uso interno para operaciones de sanitizacion de datos.
Cumple con los procedimientos de **NIST SP 800-88 Rev. 1 Guidelines for Media Sanitization**.
