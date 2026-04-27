# EC Communication Tester

EC Communication Tester is a prerelease field-test tool for EtherCAT device communication testing.

The project is built around SOEM and provides a simple desktop UI for checking slaves, reading and writing SDOs, monitoring PDO runtime status, viewing current PDO mappings, and testing basic CiA 402 profile-position motion.

## Current status

This project is currently prerelease / field-test software.

It is intended for development, commissioning support, and internal validation. It should not be treated as a certified safety component, certified EtherCAT conformance tool, or production motion controller.

## Main features

- Network adapter selection
- EtherCAT slave discovery
- Master state transition control
  - INIT
  - PRE-OP
  - SAFE-OP
  - OP
- SDO read/write
- Current PDO map status display
- PDO runtime statistics display
- PDO runtime statistics logging
- Basic CiA 402 profile-position control
  - Servo ON/OFF
  - Alarm clear
  - Absolute move
  - Incremental move
  - Jog
  - Stop
  - Homing parameter test
- Digital IO monitoring/control
- Basic analog/word IO display support
- UI log output

## Project layout

```text
SOEM_frontend/
├─ SOEM/
├─ SOEM_FrontEnd/
├─ SOEM_FrontEnd.Desktop/
└─ soem_wrap/
```

Where:

```text
SOEM/
    Third-party SOEM source code and build files.

SOEM_FrontEnd/
    Main C# application, view models, EtherCAT profile logic, SDO/PDO UI logic.

SOEM_FrontEnd.Desktop/
    Avalonia desktop entry point.

soem_wrap/
    Native wrapper DLL project used by the C# application.
```

## Build notes

### Requirements

- Windows x64
- Visual Studio 2022
- .NET 8 SDK or later
- Npcap installed on the target system
- Npcap SDK available for native wrapper build
- SOEM built for the matching configuration and platform

### Native dependency flow

```text
SOEM/build/<Configuration>/soem.lib
        ↓ linked by
soem_wrap/soem_wrap.dll
        ↓ loaded by P/Invoke
SOEM_FrontEnd.Desktop.exe
```

The C# desktop application does not link directly to `soem.lib`. It loads `soem_wrap.dll`.

### Npcap

Npcap is not bundled with this repository or release package.

Users must install Npcap separately if required.

The Npcap SDK path may need to be configured in the native wrapper project, typically through an MSBuild property such as:

```xml
<NpcapSdkDir>C:\npcap-sdk\</NpcapSdkDir>
```

## License

This project is licensed under the GNU General Public License version 3.

See [LICENSE.md](LICENSE.md).

This project uses SOEM under GPLv3. SOEM itself is dual-licensed under GPLv3 or a commercial license. The SOEM source and its original license files should remain in the `SOEM/` directory.

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Third-party software

This project uses or may interact with the following third-party software:

- SOEM - Simple Open EtherCAT Master
- Avalonia UI
- Npcap

See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Trademark notice

EtherCAT® is a registered trademark and patented technology, licensed by Beckhoff Automation GmbH, Germany.

This project is not affiliated with, endorsed by, or certified by Beckhoff Automation GmbH.

## Safety notice

This software can communicate with real motion devices and industrial IO.

Before operating real hardware:

- Verify wiring and emergency stop circuits.
- Use low speed and low force for first tests.
- Keep motion axes clear.
- Confirm limit sensor behavior.
- Confirm PDO mapping and drive state before motion.
- Do not rely on this software as a safety function.

## Prerelease note
