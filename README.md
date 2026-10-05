# Azure Dreams (PSX) — Traducción al español

Traducción no oficial apoyada con IA (fan-translation) de **Azure Dreams** (PlayStation, versión
USA, SLUS-006.14) al español, junto con la herramienta usada para crearla.

Web del proyecto: https://fjveralv.github.io/Parche-traducci-n-Azure-Dreams/

Este repositorio **no incluye ninguna imagen del disco ni archivo del juego**. Contiene:

- `AzureDreams_ES.v1.0.ppf` — el parche de traducción, en formato **PPF 3.0**.
  `AzureDreams_ES.ppf` es siempre la última versión; las anteriores se conservan con su número.
- `Tool/` — la herramienta (ejecutable y código fuente) con la que se ha hecho la traducción.
- `traduccion/` — la traducción en CSV (solo la ubicación en el disco y el texto en español, sin el
  texto original del juego), para poder regenerar o mejorar el parche.
- `DOCUMENTACION.md` — documentación técnica completa: formato del texto, punteros, offsets,
  mensajes comprimidos, fuente, gráficos y guía de la herramienta.

## Cómo aplicar el parche

Necesitas tu propia copia legal del juego en formato `.bin/.cue` (imagen RAW, 2352 bytes/sector,
**no** `.iso`).

1. Descarga un programa que aplique parches PPF, por ejemplo **PPF-O-Matic** o **MultiPatch**.
2. Selecciona como archivo a parchear tu `Azure Dreams (USA).bin` (haz antes una copia).
3. Selecciona `AzureDreams_ES.v1.0.ppf` como parche.
4. Aplica. El programa comprueba automáticamente (blockcheck) que el `.bin` es la versión correcta.
5. Juega con el `.cue` original apuntando al `.bin` ya parcheado.

## Novedades de la 1.0

- Vídeo de introducción subtitulado en español: se borra el texto inglés de la franja inferior de
  `STR/LOP2_WS.STR` y se dibujan los 45 rótulos en español (ver DOCUMENTACION.md §17). Por eso el parche
  pesa ahora unos 38 MB (el vídeo se recodifica).
- Todas las páginas de texto quedan ancladas en su sitio (0 sin anclar): las 77 que faltaban se han
  reescrito para ocupar lo mismo que el original.
- Revisión de estilo: «Torre de Monstruos» siempre con mayúsculas.

## Novedades de la Beta 4f (v0.4f)

- Revisados unos 420 textos del pueblo y de la torre para que cada cambio de página quede donde estaba
  en el original: los guiones del juego saltan a esas posiciones y, si se mueven, el juego se cuelga
  (como pasaba con Guy). Algunas frases son ahora algo más cortas.

## Novedades de la Beta 4e (v0.4e)

- Corregido el cuelgue al empezar partida nueva (escena de Guy al poner nombre al bebé): los saltos de
  los guiones del pueblo apuntan a posiciones fijas del texto y ahora cada cambio de página se queda en
  su sitio.
- Corregido el cuelgue en el tutorial de Kewne en la torre (cruceta para cambiar de dirección).
- Menú del título en español con las mismas letras del original (Nueva partida / Continuar / Opciones).
- Las versiones 4b y 4c se han retirado porque se colgaban en esas escenas.

## Novedades de la Beta 4b (v0.4b)

- Menús e interfaz en español: título (Nueva partida / Continuar / Opciones), pestañas del menú de la
  torre, Sí/No, iconos de Comprar/Vender, rótulo «Torre de Monstruos» y «CARGANDO...».
- Corregido el cuelgue de la tienda de Fur: los nombres de objeto reubicados estaban en la pila de
  matrices 3D del juego (RAM 0x8007BC70-0x8007BEF0); ahora solo se usa espacio de los propios textos de objetos.
- Corregido el descuadre del cursor en las preguntas Sí/No de la adivina: el relleno ya no pone
  espacios pasada la columna 29 (el motor los convertía en saltos de línea).
- Corregido el cuelgue cuando el viejo de los caballos ofrece construir el hipódromo.
- Corregidas las opciones que se veían mal (libro de monstruos de la hermana, encargos al
  constructor, test de la adivina…): ninguna opción ni línea pasa del ancho de la ventana.
- Corregidos los nombres de objeto que salían pegados («RojaBlancaCajaCarne», «BlancaCajaCarne»…).
- Corregidas las descripciones de trampas que mostraban «/» en lugar del salto de línea.
- Revisadas todas las líneas: máximo 31 caracteres y 3 líneas por ventana.
- Revisadas todas las elecciones (445): ninguna opción pasa de 31 caracteres ni lleva relleno detrás,
  y en las tiendas la segunda opción de «comprar/vender» empieza en la misma columna que en el original.
- Herramienta mejorada: pestañas nuevas de Mensajes de combate, Fuente y Crear parche (ver abajo).

## Estado de la traducción

- Texto de diálogos (`SLUS_006.14`, `MAIN.BIN`, `TOWN.BIN`, `DUNGEON.BIN`, enciclopedia de
  monstruos): traducido.
- Objetos (nombres, descripciones, precios): traducido.
- Mensajes de combate, objetos y estados de la torre: traducidos.
- Acentos, eñe y signos de apertura: `á é í ó ú ü ñ Ñ ¿ ¡` (las mayúsculas acentuadas, sin tilde).
- Nombres de personajes y monstruos: sin traducir, a propósito.
- Gráficos de menú/interfaz con texto: traducidos (carpeta `traduccion/imagenes`, generados con `Tool/uigen.cs`).
- Introducción del juego (vídeo): subtitulada (`traduccion/video_lop2_es.txt`, `Tool/vidsub.cs`,
  `Tool/subtitular_video.ps1`; necesita Java y jPSXdec 2.1). El resto de vídeos no tiene texto.

## La herramienta (`Tool/`)

Aplicación de escritorio para Windows (C#/WinForms, .NET Framework 4, sin dependencias). Ejecuta
`Tool/AzureDreamsTool.exe` y abre tu BIN original (nunca se modifica).

- **Texto**: todos los diálogos y menús, con medidor de espacio, códigos de control resaltados y
  avisos si una línea no cabe en la ventana del juego.
- **Objetos**: nombres, descripciones y precios; lo que no cabe se reubica solo.
- **Mensajes combate**: los 280 mensajes comprimidos de la torre.
- **Fuente**: dibuja letras nuevas y asígnalas a símbolos de la tabla del juego.
- **Imágenes / UI**: catálogo de los gráficos comprimidos del juego, editor de píxeles e
  importación/exportación PNG.
- **Tablas de datos**, **Editor hex** y **Archivos** del disco.
- **Crear parche**: carga la carpeta `traduccion/`, comprueba todo, crea el BIN traducido y el
  parche PPF verificado, y lo abre en DuckStation.

Para regenerar este parche: abre tu BIN → *Crear parche* → elige la carpeta `traduccion/` →
*Cargar carpeta en el editor* → *Comprobar* → *Crear BIN traducido* → *Crear parche PPF*.
El resultado es idéntico byte a byte al parche publicado.

Compilar: `Tool/build.bat` (usa el compilador de C# que incluye Windows). Línea de comandos y
detalles técnicos: ver [DOCUMENTACION.md](DOCUMENTACION.md).

## Créditos y licencia

- Traducción y herramienta: Fveralv.
- Investigación de la compresión de gráficos basada en hallazgos del proyecto de decompilación
  comunitario [azure-dreams-decomp](https://github.com/thingstuffs/azure-dreams-decomp); tablas de
  objetos según [azure-dreams-modding](https://github.com/RecursiveFunctions/azure-dreams-modding)
  y [adrando](https://github.com/ProGrammar-R/ProGrammar-R.github.io).
- Este proyecto no está afiliado a Konami. Azure Dreams es marca registrada de sus respectivos
  propietarios. Se distribuye únicamente el parche de diferencias, la traducción y el código de la
  herramienta, nunca datos del juego original.
