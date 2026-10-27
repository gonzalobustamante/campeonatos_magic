# Producción: MagicCampeonatos

| Recurso | Valor |
| --- | --- |
| Repositorio | `gonzalobustamante/campeonatos_magic` |
| Rama que despliega | `main` |
| Sitio IIS | `MagicCampeonatos` (el pipeline obtiene el pool asignado al sitio) |
| URL | `https://magic.chilesystems.com` |
| Ruta física inicial | `C:\inetpub\MagicCampeonatos` (el workflow consulta la ruta real en IIS) |
| Datos persistentes | `C:\ProgramData\Chilesystems\MagicCampeonatos` |
| Respaldos | `C:\ProgramData\Chilesystems\MagicCampeonatos-backups` |
| Runner dedicado en el mismo servidor | `C:\actions-runner-magic`, etiquetas `self-hosted, production` |
| Entorno GitHub | `production` |

## Funcionamiento

Cada push a `main` compila, ejecuta las pruebas y publica un artefacto en un runner Windows hospedado por GitHub. Si todo pasa, el runner de producción descarga ese artefacto, resuelve el sitio y su pool en IIS, valida las rutas, detiene únicamente ese pool, respalda código y datos, sincroniza los binarios y vuelve a iniciarlo. La aplicación aplica las migraciones de SQLite al arrancar.

Se verifica `/health/ready` contra el SHA exacto del commit desplegado y luego la portada. Un error marca la ejecución como fallida y muestra la ubicación del respaldo. Los respaldos no se borran automáticamente. No se restaura automáticamente una base después de una comprobación fallida para evitar perder escrituras realizadas tras iniciar la nueva versión.

Los pull requests solo compilan y prueban en GitHub: no ejecutan código en el runner de producción. El workflow también permite **Run workflow** sobre `main` para volver a desplegar después de completar la instalación inicial. El entorno `production` se limita a `main`.

Se siguió el patrón de [CompanyBrain Production](https://github.com/chilesystems/CompanyBrain/blob/main/.github/workflows/company-brain-production.yml) y [FinanceControl Production](https://github.com/chilesystems/ControlFinanciero/blob/master/.github/workflows/finance-control-prod.yml): validación previa, artefacto, `environment: production` y etiquetas `[self-hosted, production]`. Se usa copia local como CompanyBrain; no hacen falta contraseñas `AZURE_VM_PROD_*` ni Web Deploy en este repositorio.

## Instalación inicial en el Windows Server

1. Instalar el **.NET 10 Hosting Bundle x64** y tener IIS habilitado. No hace falta instalar el SDK para ejecutar la aplicación ni para el job de despliegue.
2. Configurar DNS para `magic.chilesystems.com` e instalar el certificado HTTPS correspondiente, o el certificado wildcard que ya usan los otros sitios.
3. Crear el sitio `MagicCampeonatos` y su pool dedicado: **No Managed Code**, **Enable 32-Bit Applications = False**, **Maximum Worker Processes = 1**. Ruta sugerida: `C:\inetpub\MagicCampeonatos`. Agregar el binding HTTPS para `magic.chilesystems.com`, puerto 443, con SNI y el certificado. Mantener autenticación anónima habilitada; no activar Windows Authentication.

   Alternativamente, desde una copia del repositorio en el servidor, ejecutar PowerShell como administrador:

   ```powershell
   .\scripts\Initialize-IisSite.ps1
   ```

   Ese script solo configura este sitio, prepara las carpetas de datos y elige un certificado válido existente para el hostname. Si el sitio ya tiene una ruta diferente, pasar `-PhysicalPath 'D:\Sitios\MagicCampeonatos'`. No modifica otros sitios.
4. Registrar **una instancia nueva** de GitHub Actions, sin reutilizar ni reemplazar la carpeta/servicio de los runners existentes:

   ```powershell
   .\scripts\Initialize-ProductionRunner.ps1
   ```

   Ejecutar como administrador en el Windows Server. El script descarga el runner oficial, verifica SHA-256 y pide el token temporal ocultando su entrada. Obtener el token de [Settings → Actions → Runners → New self-hosted runner](https://github.com/gonzalobustamante/campeonatos_magic/settings/actions/runners/new), seleccionando **Windows x64**. Copiar solo el valor después de `--token`; no es una contraseña ni un PAT. Caduca en una hora.

   Se instala un servicio exclusivo para este repositorio como LocalSystem, con permisos locales de IIS. Dejar las instrucciones de registro de otros repositorios intactas. [Referencia oficial de registro](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/add-runners) y [servicio Windows](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/configure-the-application).
5. Verificar que el nuevo runner aparece **Idle** con etiqueta `production`. La primera ejecución pendiente podrá tomarlo; si ya venció, ir a **Actions → MagicCampeonatos Production → Run workflow → main**.
6. Después del primer despliegue correcto, configurar la única cuenta administrativa en el servidor:

   ```powershell
   Set-Location C:\inetpub\MagicCampeonatos
   $env:DataDirectory = 'C:\ProgramData\Chilesystems\MagicCampeonatos'
   .\CampeonatoMagic.exe --set-admin
   Remove-Item Env:DataDirectory
   ```

   Usar la ruta física real si es diferente. El asistente pide el usuario y dos veces la contraseña, sin mostrarla. Nunca publicar esa contraseña como un secret de despliegue ni como archivo de configuración. La cuenta y los datos de la demo local no se suben al repositorio ni se instalan en producción.

## Variables de GitHub

Estas variables no son secretas y se pueden configurar en **Settings → Secrets and variables → Actions → Variables**. El workflow usa los valores de la tabla como valores predeterminados:

| Variable | Valor inicial |
| --- | --- |
| `MAGIC_IIS_SITE_NAME` | `MagicCampeonatos` |
| `MAGIC_PUBLIC_URL` | `https://magic.chilesystems.com` |
| `MAGIC_DATA_DIRECTORY` | `C:\ProgramData\Chilesystems\MagicCampeonatos` |

No cambiar `MAGIC_DATA_DIRECTORY` para un sitio que ya contiene datos sin migrarlos primero. El script rechaza cambiar una ruta persistente existente o dejar una base antigua en `App_Data` dentro del sitio.

## Datos y recuperación

`web.config` se genera para Production con la ruta persistente y el SHA de la versión; se preservan otras variables de entorno del `web.config` anterior. La sincronización excluye `App_Data`, `logs` y `appsettings.Production.json`. Datos y claves están fuera de la carpeta publicada, con acceso para SYSTEM, administradores y el pool de esta app.

Antes de tocar archivos se comprueba que la ruta de destino no sea una raíz de unidad, un directorio de sistema, un enlace/junction, ni esté compartida con otro sitio. El pool también debe ser exclusivo de esta aplicación.

Cada respaldo contiene `site/`, `data/` y `backup.json`. Para una recuperación manual: detener el pool `MagicCampeonatos`, esperar que termine su proceso, conservar una copia del estado actual, restaurar el código y, si corresponde, los datos del mismo respaldo, y volver a iniciar el pool. Restaurar datos revierte también las partidas y cambios posteriores a ese respaldo. Las claves DPAPI se restauran en el mismo servidor Windows.

## Comprobaciones locales

```powershell
dotnet test
powershell -NoProfile -File .\scripts\Test-Deployment.ps1
```

La validación de despliegue comprueba la sintaxis compatible con Windows PowerShell, las restricciones de rutas, la preservación de configuración y la exclusión de bases/credenciales del paquete. Las operaciones reales de IIS y del servicio runner requieren ejecutarse en el servidor.
