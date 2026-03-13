# OpenEye

OpenEye: Cross-Device Eye Tracking for Head-Mounted Displays
Gangtae Park, Mingyu Han, and Ian Oakley
ETRA '26: Symposium on Eye Tracking Research and Applications

## About
OpenEye is an open-source framework for **cross-device eye tracking on head-mounted displays (HMDs)**. It provides software and hardware for 3 kinds of devices include:

- Meta Quest 3
- Apple Vision Pro
- XREAL Air 2 Ultra

The repository is structured by device-specific directories. Each device directory contains directories GUI Unit, Processing Unit, and hardware mount stl files.

```
<Device>/  
├── gui_unit/
│   ├── app/
│   │   ├── app.py
│   └── core/
│       ├── config.py
│       ├── filter.py
│       ├── logger.py
│       ├── mapping.py
│       └── networking.py
│
├── processing_unit/
│
├── mount/
│   └── mount.stl
│
├── pyproject.toml
└── README.md
```
## Quick Start

### 1. Clone the repository

```bash
git clone https://github.com/witlab-kaist/OpenEye.git
cd OpenEye
```

Or if you only want to use one device (Quest / Vision Pro / XREAL), you can download only that directory using **git sparse-checkout**.

```bash
git clone --filter=blob:none --no-checkout https://github.com/witlab-kaist/OpenEye.git
cd OpenEye

git sparse-checkout init --cone

# choose one of following
git sparse-checkout set quest
git sparse-checkout set avp
git sparse-checkout set xreal

git checkout
```

### 2. Install the package

```bash
pip install -e .
```

### 3. Run the GUI application

```bash
# choose one of following
openeye-quest-gui
openeye-avp-gui
openeye-xreal-gui
```

### (Optional) Configuration

Default parameters for specific devices are dfined in:
```
<Device>/gui_unit/core/config.py
```

You can override parameters using JSON config file:

```bash
openeye-<Device>-gui --config path/to/config.json
```

Typical configurable parameters include:
- filter parameters (sampling rate, cutoff frequency)
- mapping model parameters
- evaluation task parameters
- canvas resolution

## Citation

## License