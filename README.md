# Commander · Campeonatos internos de Chilesystems

Aplicación en español para los campeonatos de Chilesystems. ASP.NET Core MVC / .NET 10, SQLite y páginas renderizadas en el servidor. No requiere Node, SQL Server ni servicios externos; estilos y scripts se sirven localmente.

**Despliegue automático:** [configuración de producción y runner](docs/PRODUCTION.md). Los pushes a `main` validan y despliegan al sitio IIS `MagicCampeonatos` en `https://magic.chilesystems.com`.

## Qué permite

- Consultar campeonatos, clasificación, reglas, jugadores e historial sin iniciar sesión.
- Administrar con una única cuenta, sin registro público de usuarios.
- Crear varios campeonatos simultáneos e inscribir a un jugador en más de uno.
- Registrar mesas de 2 a 8 participantes: ganador, eliminados, responsable de cada eliminación, sobrevivientes o retiros; fecha/hora de Chile y notas públicas.
- Editar o eliminar partidas y recalcular automáticamente la clasificación.
- Ajustar puntos con un motivo visible; eliminar un ajuste lo revierte.
- Cerrar un campeonato designando al primer lugar. Reabrirlo permite corregirlo y vuelve a quitar el campeón hasta el siguiente cierre.
- Editar, desactivar y eliminar jugadores. Eliminar los quita del registro y de nuevas inscripciones/partidas, incluso si tienen historial. Las partidas, puntos y campeones anteriores se conservan, con la referencia «eliminado». Las inscripciones sin historial se retiran. Se puede volver a registrar el mismo nombre como un jugador nuevo.
- Eliminar campeonatos desde el listado o su detalle, con confirmación explícita. Se borran sus partidas, inscripciones y ajustes, sin afectar otros campeonatos.

## Puntuación acordada

Esta implementación utiliza la variante confirmada por el equipo, no todos los reglamentos publicados por Landfall son iguales:

| Acción | Puntos |
| --- | ---: |
| Ganar la mesa | +3 |
| Cada eliminación atribuida al jugador | +1 |
| Sobrevivir cuando no hay ganador | +1 |

Los puntos son acumulables: ganar y eliminar a tres oponentes suma 6. Las eliminaciones se conservan aunque su autor después pierda. Un retiro no otorga supervivencia ni atribuye una eliminación; las eliminaciones anteriores sí se conservan. Si no se conoce al responsable de una eliminación, puede dejarse sin atribución. Cuando hay ganador no puede haber otros sobrevivientes. También se admite una mesa sin ganador donde todos hayan sido eliminados: en ese caso nadie suma por supervivencia.

Los empates de puntos comparten posición. El nombre se usa solo para ordenar visualmente a quienes empatan; no es un desempate competitivo. El administrador elige al campeón al cerrar, independientemente del líder por puntos. B3 es una etiqueta de formato: la aplicación no valida listas de mazos ni aplica restricciones de cartas.

Referencias consultadas para distinguir variantes: [Liga de Landfall](https://www.mtglandfall.com/liga-commander-mensual-magicsur/) y [evento Landfall 2026](https://www.mtglandfall.com/torneo-precon-tcg-con-chile-2/). Las reglas efectivas de esta app son las de la tabla anterior.

## Ejecutar localmente

Requiere SDK .NET 10 x64. Desde esta carpeta:

```powershell
dotnet restore
dotnet run --project CampeonatoMagic.csproj -- --set-admin
dotnet run --project CampeonatoMagic.csproj
```

El primer comando de ejecución pide usuario y contraseña dos veces, sin mostrarla. Mínimo 12 caracteres. No existe una contraseña predeterminada. El segundo inicia el sitio en la dirección que muestra la consola. El perfil de desarrollo usa HTTP local; producción exige HTTPS.

Los datos se crean automáticamente al iniciar en `App_Data/league.db`. Las migraciones de EF Core se aplican al arrancar. `App_Data/admin.json` contiene solo usuario, hash de contraseña y versión de sesión; `App_Data/keys` contiene las claves de cookies cifradas con DPAPI en Windows. Estos archivos no se publican ni se incluyen en Git. No hay datos de demostración en la base de producción.

Para restablecer la contraseña ejecuta de nuevo `--set-admin` usando la misma carpeta de datos. Esto invalida las sesiones existentes. Nunca pases una contraseña como argumento de línea de comandos ni la escribas en appsettings.

## Publicar en IIS (Windows Server x64)

1. Instala IIS y el **.NET 10 Hosting Bundle x64** en el servidor. Si IIS se instaló después del bundle, repara o reinstala el bundle. Reinicia IIS según indique el instalador.
2. En tu equipo de desarrollo publica:

   ```powershell
   .\scripts\Publish-IIS.ps1
   ```

   Copia **el contenido** de `artifacts/iis` a la carpeta del sitio, por ejemplo `C:\Sites\LaMesa`. Incluye el `web.config` generado. No copies `bin`, fuentes ni `App_Data` sobre datos existentes.
3. En IIS crea un pool dedicado `LaMesa`: **No Managed Code**, pipeline integrado, **Enable 32-Bit Applications = False** y **Maximum Worker Processes = 1**. Crea el sitio con ese pool y la carpeta publicada. Mantén **Anonymous Authentication habilitada** y Windows Authentication deshabilitada: el acceso administrativo lo controla la aplicación.
4. Configura un binding HTTPS con un certificado válido. El modo Production exige cookies seguras y redirige HTTP a HTTPS. El servidor debe estar actualizado y soportar .NET 10.
5. Guarda los datos fuera de la carpeta publicada, por ejemplo `C:\ProgramData\Chilesystems\LaMesa`. Crea la carpeta y concede **Modify** a `IIS AppPool\LaMesa`. Esa identidad necesita **Read & Execute** en `C:\Sites\LaMesa`; restringe la carpeta de datos a esa identidad, SYSTEM y los administradores. No la compartas ni la sirvas por IIS.
6. En `web.config`, dentro del elemento existente `<aspNetCore>`, agrega:

   ```xml
   <environmentVariables>
     <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
     <environmentVariable name="DataDirectory" value="C:\ProgramData\Chilesystems\LaMesa" />
   </environmentVariables>
   ```

   Si ya hay `environmentVariables`, agrega las entradas dentro de él. Conserva `processPath`, `arguments`, `hostingModel` y el handler generados por la publicación. Guarda este cambio de configuración del servidor para reaplicarlo al actualizar el sitio.
7. En una consola PowerShell del servidor configura **tu** cuenta:

   ```powershell
   Set-Location C:\Sites\LaMesa
   $env:DataDirectory = 'C:\ProgramData\Chilesystems\LaMesa'
   .\CampeonatoMagic.exe --set-admin
   Remove-Item Env:DataDirectory
   ```

   No se inicia un servidor con `--set-admin`; solo configura la cuenta. Verifica que el pool conserve los permisos sobre el archivo creado.
8. Inicia el sitio, abre su URL HTTPS, entra en **Administrar**, agrega jugadores y crea el primer campeonato. El resto podrá consultar la misma URL sin autenticación.

La publicación es dependiente del runtime: el servidor necesita el Hosting Bundle de .NET 10. Se usa el módulo ASP.NET Core y alojamiento in-process. Guía oficial: [ASP.NET Core en IIS](https://learn.microsoft.com/aspnet/core/host-and-deploy/iis/).

## Respaldos y actualizaciones

Antes de actualizar: detén el pool de esta aplicación, espera que su proceso termine y respalda **toda la carpeta de datos** (`league.db`, archivos WAL/SHM si existen, `admin.json` y `keys`). SQLite usa WAL: no copies solo el archivo `.db` mientras el sitio esté escribiendo. Copia la nueva publicación sin borrar ni sobrescribir la carpeta de datos, conserva la configuración de `web.config` y vuelve a iniciar el pool. Las migraciones futuras deben revisarse y probarse contra una copia del respaldo.

Para restaurar, detén el pool, restaura la carpeta de datos completa y vuelve a iniciarlo. Las claves DPAPI pertenecen al servidor Windows; si migras a otro equipo, conserva la base y el hash del administrador, genera nuevas claves de sesión allí y vuelve a iniciar sesión. No intentes reutilizar las claves cifradas del servidor anterior.

El diseño está pensado para una instancia de IIS y un grupo pequeño. No uses una base SQLite en un recurso compartido de red ni despliegues varias instancias escribiendo en ella.

## Verificación y mantenimiento

```powershell
dotnet test
dotnet build -c Release
dotnet tool restore
dotnet ef migrations add NombreDelCambio --project CampeonatoMagic.csproj
```

Las pruebas cubren cálculo de puntos, resultados inválidos, acceso público y privado, protección antifalsificación, edición transaccional de partidas, ajustes, cierre/reapertura, eliminación en cascada e invalidación de sesiones. Las pruebas usan una base temporal aislada y no modifican los datos reales.

Estructura: `Models` contiene entidades y puntuación, `Controllers` las operaciones públicas y administrativas, `Views` las pantallas, `Migrations` el esquema versionado y `Services/AdminAccount.cs` la única cuenta. Los resultados guardados son hechos (quién ganó, sobrevivió y eliminó); los puntos se calculan a partir de ellos.
