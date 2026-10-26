# Copyex Backup Agent

Free Windows backup agent that copies folders to an external drive or network share (NAS) on a schedule.

Programa gratuito para Windows que hace copias de seguridad automáticas de carpetas en un NAS o disco externo.

Desarrollado y mantenido por **Copyex Ofimática S.L.** (España).

## Características

- Cada copia se guarda en una carpeta nueva con fecha y hora (`AAAA-MM-DD_HH-MM-SS`).
- Mientras se copia, la carpeta lleva el sufijo `_INCOMPLETE`: una copia a medias nunca se confunde con una buena.
- Conserva el número de copias que elijas y borra las más antiguas **solo de este equipo**.
- Copia automática con el Programador de tareas de Windows: diaria o semanal, despierta el equipo y recupera la copia si estaba apagado.
- Opción de apagar el equipo al terminar, con aviso de 60 segundos para cancelar.
- Credenciales del NAS guardadas cifradas con la cuenta de Windows del usuario (DPAPI).
- Requiere .NET Framework 4.8 (incluido en Windows 10 y 11).

## Descarga

Las versiones publicadas están en [Releases](../../releases). Descarga `CopyexBackupAgent_Setup.exe` y ejecútalo como administrador.

Desde la versión firmada, los ejecutables llevan la firma de código de SignPath Foundation (ver más abajo).

## Compilar

Requisitos: Windows con .NET Framework 4.8 y, para el instalador, [NSIS 3](https://nsis.sourceforge.io).

```bat
COMPILAR.bat
```

Genera `bin\CopyexBackupAgent.exe` y, si NSIS está instalado, `CopyexBackupAgent_Setup.exe`.
Las versiones oficiales se compilan automáticamente en GitHub Actions (`.github/workflows/build.yml`).

## Datos del programa

| Fichero | Contenido |
|---|---|
| `%APPDATA%\CopyexBackupAgent\config.xml` | Configuración |
| `%APPDATA%\CopyexBackupAgent\history.xml` | Resumen de las últimas 100 ejecuciones |

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io),
certificate by [SignPath Foundation](https://signpath.org).

- Committers and reviewers: [Copyex team members](../../graphs/contributors)
- Approvers: Copyex Ofimática S.L. (informatica@copyex.es)

Every signed release is built from this repository's source code by GitHub Actions
and manually approved before signing.

## Privacy policy

This program will not transfer any information to other networked systems unless
specifically requested by the user or the person installing or operating it. It only
connects to the backup destination (local disk or network share) configured by the user.

## License / Licencia

Copyright © 2026 Copyex Ofimática S.L.

This program is free software: you can redistribute it and/or modify it under the terms
of the GNU Affero General Public License version 3, as published by the Free Software
Foundation. See [LICENSE](LICENSE).

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
See the GNU Affero General Public License for more details.

Este programa es software libre: puede redistribuirlo y/o modificarlo bajo los términos de
la Licencia Pública General Affero de GNU, versión 3. Se distribuye **sin ninguna garantía**.
El texto legal válido es el original en inglés del fichero [LICENSE](LICENSE).

## Trademarks

"Copyex" and the Copyex logo are trademarks of Copyex Ofimática S.L. The AGPL-3.0
license covers the source code and assets only and does not grant permission to use
these trademarks. Forks and redistributions must remove the Copyex name and logo.

"Copyex" y el logotipo de Copyex son marcas de Copyex Ofimática S.L. La licencia AGPL-3.0
no concede ningún derecho sobre ellas: las versiones modificadas o redistribuidas deben
quitar el nombre y el logotipo de Copyex.
