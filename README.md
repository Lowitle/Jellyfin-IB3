# IB3 para Jellyfin

Plugin de Jellyfin que replica el catálogo a la carta de [IB3](https://totib3.org) como una biblioteca de
series: un `.strm` por vídeo con la URL del MP4 progresivo, un `.nfo` por episodio, un `tvshow.nfo` con el id
del programa, la carátula y el fondo de la serie, la miniatura de cada episodio y los subtítulos de IB3
convertidos de WebVTT a SRT.

## Requisitos

- Jellyfin 10.11 (`targetAbi` 10.11.0.0, el plugin se compila para `net9.0`).
- Una carpeta del servidor donde Jellyfin pueda escribir, fuera de las carpetas del propio Jellyfin si se
  quiere que la biblioteca solo indexe lo que escribe el plugin.

## Instalación

### Desde el repositorio de plugins

1. Panel de control → **Plugins** → **Catálogo** → **Ajustes** → **Repositorios** y añade:
   `https://raw.githubusercontent.com/Lowitle/Jellyfin-IB3/main/manifest.json`
2. En el catálogo instala **IB3** y reinicia Jellyfin.

### Manual

1. Descarga el `.zip` de la última versión (o genéralo con `package.ps1`, ver más abajo).
2. Copia su contenido en `plugins/IB3_<versión>/` dentro del directorio de datos de Jellyfin y reinicia.

## Configuración

La página del plugin (**Panel de control → Plugins → IB3**) hace todo el recorrido:

1. **Carpeta de destino**: escribe la ruta y pulsa **Comprobar**. La página dice si la carpeta existe, si
   Jellyfin puede escribir en ella y, cuando no, qué carpetas escribibles hay en el servidor.
2. **Biblioteca**: con la carpeta escrita, **Crear biblioteca** crea la biblioteca «IB3» (el nombre se puede
   cambiar) con las ocho carpetas de las secciones como carpetas de la biblioteca —no la carpeta de
   destino—, o ajusta la biblioteca que ya se llama así: le añade las carpetas que le falten y le quita las
   que la configuración ya no escribe. Así la biblioteca que creó la versión 1.1.0.0 (que apuntaba a la
   carpeta de destino) pasa a indexar las secciones con un clic, sin deshacerla a mano.
3. **Secciones y bibliotecas**: **Ver secciones y bibliotecas** abre una tabla con las ocho carpetas de
   sección; cada fila dice si la carpeta existe, si la biblioteca «IB3» la indexa, qué bibliotecas del
   servidor la contienen y ofrece un desplegable con todas ellas para **añadir** la sección a otra
   biblioteca (`Documentals` a una biblioteca de documentales, `Arxiu` a una de archivo…). Jellyfin indexa
   la misma carpeta en todas las bibliotecas que la apuntan, así que la sección sigue en «IB3» y no se
   copia ningún archivo; para quitarla de una biblioteca se usa **Panel de control → Bibliotecas**.
4. **Secciones**: la lista de secciones del catálogo («Actualitat», «Retransmissions», «Sèries», «Cinema»,
   «Documentals», «Programes», «Destacat» y «Arxiu») con sus subcategorías. Al elegir una sección —o una de
   sus subcategorías— la página enseña los programas que publica y se pueden añadir a la lista de uno en uno
   o todos de golpe, sin buscarlos por su nombre.
5. **Programas**: busca por título o slug y elige el programa de los resultados; también se puede pegar la
   dirección de su página de totib3.org. Antes de añadirlo se puede **probar**, y la página enseña cuántos
   vídeos publica IB3, cuántos se pueden añadir y cuántos han caducado. Cada programa puede fijar su propia
   sección, y quitarlo de la lista borra su carpeta en la ejecución siguiente.
6. **Opciones**: .nfo, bloqueo de los metadatos en Jellyfin (los NFO llevan `lockdata`), subtítulos,
   imágenes, organización por sección (una subcarpeta por cada sección de totib3.org) y borrado de los
   archivos de los vídeos que IB3 ha despublicado.
7. **Guardar** o **Ejecutar ahora**. La tarea programada repite la actualización cada N horas.

La biblioteca queda como en la plataforma digital de IB3: ocho carpetas de primer nivel, una por sección, y
dentro una carpeta por programa, nunca un nivel más. La subcategoría de IB3 (`Cuina`, `Entreteniment`,
`Documentals històrics`...) es un género y una etiqueta de la serie, no una carpeta: Jellyfin lee cualquier
carpeta que solo agrupe series como una serie más y la muestra vacía junto a las de verdad. Las ocho carpetas
son las rutas de la biblioteca, no la carpeta de destino: si la biblioteca apuntara a la carpeta madre,
Jellyfin leería cada nombre de sección como si fuera una serie. Cuando IB3 publica un programa en dos
secciones —cinco programas deportivos están en «Actualitat» y en «Programes»— manda el orden del menú, y la
sección que se fije a mano en la página manda siempre sobre la del catálogo. Como Jellyfin indexa una carpeta
en cada biblioteca que apunta a ella, la tabla de **Secciones y bibliotecas** añade una sección a una
biblioteca que ya exista («IB3 Documentals» que solo apunte a `Documentals`, la de dibujos que apunte a
`Programes`…) sin duplicar ni un archivo ni quitarla de «IB3».

La primera ejecución tarda lo que IB3 tarde en servir el catálogo de cada programa; las siguientes solo
descargan las imágenes y los subtítulos que faltan.

## Metadatos

Los `.nfo` se construyen solo con lo que publica el catálogo de IB3 (`ib3media.org/api`, la API que usa
totib3.org): el plugin no consulta TMDB, TVDB ni IMDb.

| Fichero | Qué lleva |
| --- | --- |
| `tvshow.nfo` | título, sinopsis, año y fecha de estreno (del primer capítulo publicado), clasificación por edad (`mpaa`: `7`, `12`, `16`, `18` o `TP`), país, estudio, géneros (la sección y las subcategorías de IB3), etiquetas, `namedseason` («Temporada 1»…) y el `uniqueid` del programa |
| `Season N/season.nfo` | el número y el nombre de la temporada |
| `<capítulo>.nfo` | título, sinopsis, temporada, episodio, fecha de emisión, duración, miniatura y el `uniqueid` del vídeo |
| `poster.jpg` | la carátula del programa, descargada del catálogo de IB3 |

El `tvshow.nfo` lleva `lockdata` por defecto. Jellyfin lo lee (`IsLocked`) y deja de reemplazar el título,
la sinopsis, el año y el póster con los de un proveedor online: es lo que impide que «Dona», «Ben» o
«Ciclisme» —programas de IB3 con nombre de otras obras— se conviertan en una serie distinta. La opción
**Bloquear los metadatos en Jellyfin** lo desactiva para una biblioteca que quiera enriquecerse con TMDB.

La numeración sigue siendo la de IB3 (un informativo diario llega a `S01E5534`), que no coincide con la de
ningún proveedor: por eso el `uniqueid` propio y el bloqueo son la ficha que Jellyfin muestra. Cruzar el
catálogo con `open-tv-ids` para escribir también los ids de TMDB, TVDB e IMDb no está implementado todavía.


```powershell
dotnet build Jellyfin.Plugin.Ib3.sln -c Release
./package.ps1
```

`package.ps1` compila en `Release` y deja `dist/ib3_<versión>.zip` con el ensamblado. La página de
configuración viaja dentro del `.dll` (es un `EmbeddedResource` del proyecto), así que el paquete es un
único archivo.

La versión vive en `Directory.Build.props`; `build.yaml`, `manifest.json` y `Properties/AssemblyInfo.cs`
tienen que decir lo mismo.

## Notas

- **Metadatos**: los NFO se construyen solo con lo que publica el catálogo de IB3 (`ib3media.org/api`, la
  API que usa totib3.org): título, sinopsis, año y fecha de estreno (del primer capítulo publicado),
  clasificación por edad, géneros, etiquetas, temporadas nombradas y el `uniqueid` del programa y de cada
  vídeo. El `tvshow.nfo` lleva `lockdata` por defecto, así que Jellyfin no reemplaza esos datos con los de
  TMDB o TVDB; si se prefiere que manden los proveedores online, se desactiva **Bloquear los metadatos en
  Jellyfin** y se vuelve a ejecutar la tarea.
- **Numeración**: manda el número que publica IB3; un vídeo numerado con un código de producción toma el
  siguiente número libre de su temporada, en orden de emisión. Como esa numeración no coincide con TVDB ni
  con TMDB, los episodios se numeran con la de IB3 y el bloqueo de metadatos es lo que mantiene la ficha
  local.
- **Geobloqueo y caducidad**: si IB3 no publica MP4 de un vídeo, el plugin lo omite, lo anota en el registro
  y lo reintenta en la ejecución siguiente.
- **Borrado**: el plugin solo borra carpetas que escribió él y cuyo `tvshow.nfo` lleva el id del programa.
- **Actualizar desde la 1.3.0 o anterior**: las carpetas de subcategoría (`Programes/Cuina`,
  `Documentals/Història`...) ya no se escriben. La primera ejecución reescribe cada serie en la carpeta de su
  sección, borra la carpeta que escribió la vez anterior —el plugin la recuerda en `WrittenPath`— y elimina la
  carpeta de subcategoría cuando se queda vacía, así que Jellyfin deja de listar `Cuina` o `Entreteniment`
  como series. Una carpeta que todavía guarde una serie que ya no está en la lista se queda como está: se
  borra a mano desde el sistema de archivos.
- **Actualizar desde la 1.0**: la primera ejecución reescribe cada serie en la carpeta de su sección y borra
  la carpeta que escribió la vez anterior, que el plugin recuerda en `WrittenPath`; las series antiguas no se
  duplican. Las carpetas del layout anterior (`Informatius`, `Infantil`, `Esports`...) quedan vacías y se
  pueden borrar a mano, y los nombres antiguos de la configuración se siguen leyendo: se mapean a la sección
  que les corresponde.

## Repositorio

| Archivo | Para qué |
| --- | --- |
| `Directory.Build.props` | Versión del plugin (la leen el `.csproj`, `package.ps1` y el workflow de release). |
| `Jellyfin.Plugin.Ib3.sln` | Solución con el único proyecto del plugin. |
| `Jellyfin.Plugin.Ib3/Client/Ib3Categories.cs` | Tabla de las ocho secciones de totib3.org y sus subcategorías: la sección es la que nombra la carpeta, la subcategoría solo va a los géneros de la serie. |
| `package.ps1` | Compila en `Release` y deja `dist/ib3_<versión>.zip`. |
| `scripts/Update-Manifest.ps1` | Inserta en `manifest.json` la versión con su MD5 y su `sourceUrl`. |
| `scripts/Release.ps1` | Publica una versión: sube el número, empaqueta, actualiza el manifiesto, commitea y empuja el tag. |
| `build.yaml` | Ficha del plugin para las herramientas de empaquetado de Jellyfin. |
| `.github/workflows/build.yaml` | Compila y publica el `.zip` como artefacto en cada push. |
| `.github/workflows/release.yaml` | Con un tag `v*` (o a mano) comprueba la versión, empaqueta, actualiza `manifest.json`, crea la release de GitHub y hace commit del manifiesto. |

## Publicar una versión

El repositorio publica cada versión en **Releases** y el `manifest.json` de `main` es el catálogo que
Jellyfin lee, así que una versión nueva aparece en el panel de control de cualquier servidor que tenga el
repositorio añadido (y en el resto, al recargar el catálogo).

1. Escribe la entrada de la versión en `manifest.json` (`version` y `changelog`); el changelog se conserva
   al publicar, así que es el sitio donde se explica el cambio. `targetAbi` es `10.11.0.0`.
2. Lanza:

```powershell
./scripts/Release.ps1 -Version 1.1.0.0
```

El script sube el número en `Directory.Build.props`, `Properties/AssemblyInfo.cs` y `build.yaml`, compila,
empaqueta, recalcula el MD5 de `manifest.json`, hace el commit, crea el tag `v1.1.0.0` y lo empuja. El tag
dispara `.github/workflows/release.yaml`, que vuelve a compilar (en Linux, así que el binario no es el mismo
que el local y el checksum del manifiesto se recalcula solo), crea la release con el `.zip`, refresca el
manifiesto en `main` y lo commitea. Con `-NoPush` se puede ensayar sin publicar nada.

También vale a mano: subir la versión en esos tres archivos y empujar el tag `v<versión>`.


