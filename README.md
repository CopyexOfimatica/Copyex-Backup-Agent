# Copyex Backup Agent

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

## Política de firma de código

Firma de código gratuita proporcionada por [SignPath.io](https://about.signpath.io),
con certificado de [SignPath Foundation](https://signpath.org).

- Desarrolladores y revisores: [miembros del equipo de Copyex](../../graphs/contributors)
- Aprobadores: Copyex Ofimática S.L. (informatica@copyex.es)

Cada versión firmada se compila a partir del código fuente de este repositorio con GitHub Actions
y se aprueba manualmente antes de firmarla.

## Política de privacidad

Este programa no transmite ninguna información a otros sistemas de la red salvo que lo solicite
expresamente el usuario o la persona que lo instala o lo utiliza. Solo se conecta al destino
de la copia (disco local o carpeta de red) configurado por el usuario.

## Licencia

Copyright © 2026 Copyex Ofimática S.L.

Este programa es software libre: puede redistribuirlo y/o modificarlo bajo los términos de
la Licencia Pública General Affero de GNU, versión 3, publicada por la Free Software Foundation.
Consulte el fichero [LICENSE](LICENSE).

Este programa se distribuye con la esperanza de que sea útil, pero **SIN NINGUNA GARANTÍA**,
ni siquiera la garantía implícita de COMERCIABILIDAD o IDONEIDAD PARA UN FIN DETERMINADO.
Consulte la Licencia Pública General Affero de GNU para más detalles.
El texto legal válido es el original en inglés del fichero [LICENSE](LICENSE).

## Marcas registradas

"Copyex" y el logotipo de Copyex son marcas de Copyex Ofimática S.L. La licencia AGPL-3.0
cubre únicamente el código fuente y los recursos, y no concede ningún derecho sobre estas marcas:
las versiones modificadas o redistribuidas deben quitar el nombre y el logotipo de Copyex.
