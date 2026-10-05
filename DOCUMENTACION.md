# Azure Dreams (PSX, USA) — Documentación técnica y guía de la herramienta

Este documento explica **cómo está guardado el texto, los objetos, los mensajes, la fuente y los
gráficos** en Azure Dreams (PlayStation, versión USA, SLUS-006.14), **dónde están los punteros y
offsets**, qué límites tiene el motor del juego y **cómo usar la herramienta** (`Tool/`) para
hacer una traducción completa como la española, o retocarla.

Todo lo que aparece aquí está verificado sobre el disco USA (`Azure Dreams (USA).bin`, 126.946
sectores). Los números van en hexadecimal (`0x…`) salvo que se diga otra cosa.

---

## Índice

1. [Resumen rápido](#1-resumen-rápido)
2. [El disco](#2-el-disco)
3. [Mapas de memoria (archivo ↔ RAM)](#3-mapas-de-memoria-archivo--ram)
4. [Texto de diálogos y menús](#4-texto-de-diálogos-y-menús)
5. [Límites del motor (lo que provoca cuelgues)](#5-límites-del-motor-lo-que-provoca-cuelgues)
6. [Objetos: nombres, descripciones y precios](#6-objetos-nombres-descripciones-y-precios)
7. [Mensajes de combate y de la torre](#7-mensajes-de-combate-y-de-la-torre)
8. [Textos extra (no detectables)](#8-textos-extra-no-detectables)
9. [La fuente y la tabla de caracteres (á é í ó ú ü ñ Ñ ¿ ¡)](#9-la-fuente-y-la-tabla-de-caracteres)
10. [Gráficos e interfaz (formato comprimido)](#10-gráficos-e-interfaz-formato-comprimido)
11. [Parches PPF](#11-parches-ppf)
12. [La herramienta: uso paso a paso](#12-la-herramienta-uso-paso-a-paso)
13. [Formato de los archivos de traducción (CSV)](#13-formato-de-los-archivos-de-traducción-csv)
14. [Línea de comandos](#14-línea-de-comandos)
15. [Código fuente: qué hace cada archivo](#15-código-fuente-qué-hace-cada-archivo)
16. [Pendiente y créditos](#16-pendiente-y-créditos)
17. [Vídeo de introducción (subtítulos)](#17-vídeo-de-introducción-subtítulos)

---

## 1. Resumen rápido

| Qué | Dónde | Formato | Cómo se modifica |
|---|---|---|---|
| Diálogos, menús, tiendas | `SLUS_006.14`, `MAIN/MAIN.BIN`, `TOWN/TOWN.BIN`, `DUNGEON/DUNGEON.BIN` | Shift-JIS de ancho completo + códigos de control | En su sitio, mismo tamaño; el sobrante se rellena |
| Objetos | Tabla en RAM `0x80073414` (SLUS) | Registros de 20 bytes con punteros | Los textos largos se reubican y se corrige el puntero |
| Mensajes de combate | `DUNGEON.BIN` `0xFAD02`–`0xFC9B6` | Comprimido con tabla de 54 letras | En su sitio, tamaño exacto |
| Fuente de los diálogos | `MAIN.BIN` `0x2257D8` y `DUNGEON.BIN` `0x4C27D8` | LZ propio, 128×128 a 4 bpp | Recomprimida (debe caber en su hueco) |
| Tabla de caracteres | SLUS, RAM `0x8007142C` (claves) / `0x8007147C` (valores) | 40 códigos SJIS → celda | Se redirigen 10 símbolos a letras nuevas |
| Gráficos de menú/UI | Directorios en `MAIN.BIN` `0x221800`, `DUNGEON.BIN` `0x4BE800` | LZ propio | Editor de imágenes de la herramienta |
| Vídeos (intro) | `STR/LOP2_WS.STR` | MDEC (FMV) | Texto quemado en la imagen: se rehacen los fotogramas con jPSXdec (§17) |

---

## 2. El disco

- Imagen **BIN/CUE MODE2/2352** (no ISO). Cada sector ocupa 2352 bytes: 24 de cabecera (sync,
  dirección, subcabecera), 2048 de datos, y EDC (4 bytes) + ECC (276 bytes) al final.
- Sistema de archivos ISO 9660 normal. La herramienta lo lee directamente del BIN.
- Al escribir, la herramienta **recalcula EDC y ECC** de cada sector tocado (Mode 2 Form 1), así
  el BIN parcheado es válido en consola real y en emuladores estrictos (`Disc.cs`, clase `Ecc`).
- Conversión de un offset "crudo" del BIN a (archivo, offset):
  `lba = raw / 2352`, `dentro = raw % 2352 − 24`, offset de archivo = `(lba − lba_inicio_archivo) × 2048 + dentro`.

| Archivo | LBA | Tamaño | Contenido |
|---|---|---|---|
| `SLUS_006.14` | 24 | 512 KB | Ejecutable: menús, ayuda, objetos, tabla de caracteres |
| `MAIN/MAIN.BIN` | 1817 | 2,5 MB | Datos generales: fuente, gráficos, nombres de huevos/familiares |
| `TOWN/TOWN.BIN` | 3077 | 16 MB | Pueblo (Monsbaiya): scripts de diálogo de todos los personajes |
| `DUNGEON/DUNGEON.BIN` | 12310 | 26,5 MB | Torre: mensajes de combate, scripts, copia de la fuente |
| `OVMOVIE.BIN` | 280 | 3 MB | Reproductor de vídeo |
| `STR/*.STR`, `STRT/*.STR` | — | ~230 MB | Vídeos FMV |

> `KCET`, `KDT` y `TSDB` dentro de MAIN/TOWN/DUNGEON **son bancos de sonido**, no gráficos.

---

## 3. Mapas de memoria (archivo ↔ RAM)

El código del juego usa **direcciones de RAM absolutas** para encontrar textos y datos. Saber qué
dirección corresponde a cada offset es lo que permite localizar punteros.

| Bloque | Fórmula | Notas |
|---|---|---|
| SLUS (ejecutable) | `RAM = 0x8002C800 + offset` | Válido de `0x8002D000` a `0x80081800` |
| MAIN residente | `RAM = 0x8000B800 + offset` | Hasta `0x8002D000` (nombres de huevos, familiares) |
| TOWN, región de Wreath (casa) | `RAM = offset + 0x7FC31000` | Overlay en la ventana `0x80010000–0x8001FFFF` |
| DUNGEON, mensajes de combate | `RAM = offset + 0x7FFE5760` | Ej.: `0xFAD02` → `0x800E0462` |

Cargas observadas en un volcado de RAM: MAIN `0x20800` → `0x8002C000`; TOWN `0xB000` →
`0x80088760`; DUNGEON `0x51D800` → `0x8002C000`; OVMOVIE `0` → `0x80176800`. TOWN y DUNGEON
cargan **overlays** distintos según la zona, así que cada región tiene su propia base.

**Tipos de puntero que usa el juego**

- **Palabras de 32 bits alineadas** (tablas): por ejemplo, los registros de objetos. En SLUS y MAIN
  la mayoría de las cadenas tienen un puntero así (la pestaña Texto lo muestra para cada una:
  «N puntero(s) → RAM 0x…»).
- **Pares `lui`/`addiu` en el código MIPS** (direcciones partidas en dos instrucciones): así apunta
  el código a cada mensaje de combate y a cada mensaje interno de los scripts de TOWN. No se pueden
  "corregir" fácilmente, por eso esos textos **no se mueven nunca de sitio**.
- **Scripts de TOWN/DUNGEON**: el texto va incrustado en un bytecode; las órdenes de salto
  (`0x15`, `0x16`, `0x17`) y llamadas (`0x4C` + dirección) llevan direcciones internas.

Consecuencia práctica: **cada texto se escribe en su offset original y ocupando exactamente los
mismos bytes**. Lo que sobra se rellena (ver §5). Solo los textos de objetos se reubican.

---

## 4. Texto de diálogos y menús

### 4.1 Codificación

El inglés de la versión USA está en **Shift-JIS de ancho completo**: `Ｉｔ` = `82 68 82 94`.
La herramienta muestra texto normal y lo convierte al escribir (espacio = `81 40`).
Quedan restos del texto **japonés** original en el disco (sobre todo en TOWN); la herramienta los
oculta por defecto.

### 4.2 Códigos de control

Dentro de un texto, los bytes menores de `0x80` no son letras sino órdenes. En la herramienta se
escriben entre llaves.

| Byte(s) | Etiqueta | Significado |
|---|---|---|
| `00` | — | Fin del texto (vuelve al script) |
| `01` | `{01}` | **Fin de mensaje.** El siguiente mensaje empieza justo después y el código lo localiza por su dirección (ver §5.3) |
| `08` | `{VENT}` | Abre/limpia la ventana de mensaje |
| `0A` | `⏎` | Salto de línea |
| `0B` | `{0B}` | Empieza una opción de una elección: `[Sí]` |
| `0F xx yy` | `{0F}{xx}{yy}` | Cambia retrato/expresión (2 bytes de argumento) |
| `11` | `{PAG}` | Espera a que se pulse un botón (cambio de página) |
| `57 xx` | `{57}{xx}` | Elige quién habla (1 byte de argumento). `57 01` antes de una elección = responde el héroe |
| `FE 00` / `FE 01` | `{HEROE}` / `{HEROE2}` | Nombre del héroe (hasta 8 letras) |
| `4C` + 4 bytes | — | Llamada a una función del juego (p. ej. calcular un precio). Separa dos textos |
| `FD 0F` | — | Imprime un número (precio, nivel…) |
| `15`, `16`, `17`, `19`, `1A`… | — | Bytecode del script (saltos, condiciones). No forman parte del texto |

Ejemplo real (TOWN `0x66DE97`, el viejo de los caballos):
```
"It'll cost "  4C 34 61 01 80  FD 0F  " G." ⏎ 57 01 0B "[I don't have enough money.]" ⏎ 0B "[Money is not a concern.]"
```
Son **dos entradas** de texto ("It'll cost " y " G.⏎…") separadas por la llamada que calcula el
precio. Hay que traducir cada una en su sitio.

### 4.3 Cómo se localizan los textos (detector)

`SjisCodec.Scan` (`TextTools.cs`) recorre cada archivo buscando secuencias de pares Shift-JIS
válidos (`81 40–AC`, `82 4F–F1`, `83 40–96`). Une en un mismo texto los tramos separados por
códigos de control (`01–14`, `57`, `FE xx`), hasta que aparece un byte que no es texto ni control.
Cada texto detectado guarda: archivo, offset, longitud en bytes (= hueco disponible) y los bytes
originales. Se descartan los de menos de 3 caracteres y se marcan como japoneses los de kana/kanji.

Resultado en el disco USA: **5.082 textos en inglés** (SLUS 786, MAIN 217, TOWN 3.063, DUNGEON 1.016).

### 4.4 Relleno

Si la traducción ocupa menos que el original, el resto se rellena para que el siguiente dato del
script quede donde estaba. Orden que usa la herramienta (`SjisCodec.PadTo`):

0. Cadenas "puras" de SLUS y MAIN (ayudas, menús: texto y saltos de línea, sin códigos de script) que en
   el original **terminan en `00`**: se leen por puntero hasta el `00`, así que se rellenan con `00`. Si lo
   que sigue no es `00` (p.ej. `11` {PAG}), el texto es parte de un guion y un `00` de relleno sería una
   instrucción basura: así se colgaba el tutorial de Kewne en la torre (v0.4b-v0.4d).
0b. **Páginas en su sitio**: cada `{PAG}` (`11`) se deja en la misma posición que en el original y el
   relleno se reparte página a página (`PadPages`). Los guiones de eventos de `TOWN.BIN` saltan con
   **direcciones absolutas** (`15 dir`, `17 dir`, `3E xx dir`; `4C dir` llama a código) al `{PAG}` o a lo que
   le sigue. Si se mueve, el salto cae en mitad del texto y el juego se cuelga (escena de Guy al poner
   nombre al bebé, v0.4b-v0.4c). Cada bloque se carga detrás del código de su zona (p.ej. TOWN `0x427800` →
   RAM `0x80017B60`), y la dirección de carga no está en ningún índice. Por eso se anclan todas las
   páginas posibles, eligiendo con programación dinámica la combinación sin relleno de último recurso.
   El informe dice cuántas `páginas sin anclar` quedan; lo ideal es 0. Para anclar una página, su
   traducción (también el último trozo del texto) debe caber en los bytes de la original y poder
   rellenarse: con espacios hasta la columna 29 o con un `{VENT}` extra tras el `{PAG}` (el original usa
   `{PAG}{VENT}{VENT}`). Desde la v1.0quedan 0: las páginas cuyo original tenía líneas de 30-31 letras se han
   reescrito para llegar a esas columnas (o con opciones más largas que las originales). Las páginas
   ajustadas están en `trad/t_067.txt`.
1. Espacios (`81 40`) al **final de las líneas de texto**, sin pasar de la **columna 29** (ver §5.1).
2. **Líneas en blanco** (salto + espacios) al final de ventanas sin opciones que tengan menos de 3 líneas.
3. Hasta dos `{VENT}` extra detrás de cada `{PAG}{VENT}` (el original tiene cientos de `{VENT}{VENT}{VENT}`).
4. Si la entrada solo tiene opciones: espacios detrás de cada opción sin pasar del ancho que tenía
   esa opción en el original ni de la columna 29.
5. Último recurso (la herramienta lo cuenta en el informe; en la traducción española no queda ninguno):
   espacios en la última línea. Si ocurre, alarga un poco la traducción para llenar el hueco.

**Nunca** se ponen espacios detrás de una opción `[…]` más allá de su ancho original (ver §5.2).

---

## 5. Límites del motor (lo que provoca cuelgues)

Estos fallos aparecieron durante la traducción. La herramienta los evita o avisa de ellos.

### 5.1 Ventana: 31 caracteres por línea, 3 líneas

En el original ninguna línea de diálogo pasa de 31 caracteres (`{HEROE}` cuenta como 8) y cada
ventana muestra 3 líneas. La pestaña Texto marca en **naranja** las traducciones que lo superan
(filtro «Con avisos») y «Comprobar» las lista.

**Espacios al final de línea:** si una línea llega a las columnas 30-31 con **espacios**, el motor salta
de línea por su cuenta y el salto explícito añade otra: aparece una línea de más que empuja el texto
hacia arriba y descuadra el cursor de las opciones (así pasaba en las preguntas Sí/No de la adivina).
En el original casi nunca hay espacios en esas columnas (3 de ~4.000 líneas). Por eso el relleno no pasa
de la columna 29. Las líneas de 30-31 **letras** sí son válidas.

### 5.2 Opciones de elección

Una elección es `57 01` seguido de líneas que empiezan por `0B`:
```
¿Qué pasa? ⏎ {57}{01}{0B}[Nada.] ⏎ {0B}[¡Construyo un hipódromo!]
```
El juego mide cada opción hasta el siguiente salto o el final del texto: **los espacios de relleno
detrás de la última opción cuentan como parte de ella**. Si la opción queda más ancha que la
ventana, la memoria se corrompe y el juego **se cuelga** poco después. Así se colgaba la oferta del
hipódromo en la v0.3 («[¡Construyo un hipódromo!]» + 7 espacios = 33 caracteres).
Reglas: opciones de 31 caracteres como máximo y sin relleno detrás.

Si un texto empieza directamente por `[` (el `0B` quedó justo antes del offset), también es una
opción: la herramienta lo detecta.

### 5.3 Mensajes internos (`{01}`)

Muchos textos de TOWN contienen varios mensajes separados por `01`. El código **salta a cada
mensaje por su dirección absoluta**: si la traducción de uno es más larga o más corta y desplaza
a los siguientes, el juego lee basura (corazones en pantalla y cuelgue, como pasaba con Wreath al
abrir la caja fuerte). Solución (`SjisCodec.PadMessages`): cada mensaje se rellena en **su propio
hueco** y todos los `01` quedan en su posición original. Si un mensaje no cabe en su hueco, hay
que acortarlo (la herramienta lo marca en rojo: «un mensaje interno no cabe»).

Ojo: tras un `01` puede haber bytes de script antes del texto siguiente (p. ej. `{01}{0D}⏎{09}…`
o `{01}f{57}…`); hay que conservarlos tal cual.

Argumentos que **no** son códigos: `57` lleva 1 byte, `0F` lleva 2 y `FE` lleva 1. Por eso
`{57}{01}` no termina un mensaje y `{0F}{0B}{03}` no es una opción.

### 5.4 `¿` y `¡` al abrir una ventana

Si el **primer carácter** dibujado tras abrir una ventana o página (`{VENT}`, `{PAG}`, inicio) es
`¿` o `¡`, el motor no redibuja bien y se ven restos de la ventana anterior (p. ej. «[No.]»). La
herramienta quita ese signo de apertura automáticamente en esa posición.

### 5.5 Nombres de objeto reubicados

Las versiones hasta la v0.4a copiaban los nombres que no cabían a una zona del SLUS que estaba a ceros
(RAM `0x8007BCB0`–`0x8007BEF0`). **No es espacio libre:** es la pila de matrices de la librería gráfica
(`PushMatrix`/`PopMatrix`, 20 matrices de 32 bytes desde `0x8007BC70`; justo después está el mensaje
«Can't push matrix, stack (max 20) is full!»). En escenas 3D como la tienda de Fur el juego escribía
matrices encima de los nombres y se colgaba. Desde la v0.4b solo se usa espacio de los propios textos
de objetos (§6.3). Antes de usar una zona "a ceros", comprueba en los símbolos de la descompilación
y en volcados de RAM que no la usa nada en tiempo de ejecución.

---

## 6. Objetos: nombres, descripciones y precios

### 6.1 Tabla de categorías

En el SLUS, RAM `0x80073414` (offset `0x46C14`): 22 registros de 20 bytes (el 0 está vacío).

| Offset | Tamaño | Campo |
|---|---|---|
| `+0x02` | u8 | Número de objetos de la categoría |
| `+0x04` | u32 | Puntero al nombre de la categoría |
| `+0x0C` | u32 | Puntero al array de objetos |
| `+0x10` | u32 | Clase de uso |

Categorías: 1 Hierbas, 2 Comida de familiar, 3 Semillas, 4 Bolas, 5 Pergaminos, 6 Cristales,
7 Campanas, 8 Gafas, 9 Lupas, 10 Arena, 11 Regalos, 12 Especiales, 13 Misiones, 14 Monedas,
15 Espadas, 16 Varitas, 17 Escudos, 18 Huevos, 19 Familiares, 20 Ascensores, 21 Trampas.

### 6.2 Registro de objeto (20 bytes)

| Offset | Tamaño | Campo |
|---|---|---|
| `+0x00` | u8 | Id |
| `+0x04` | u32 | Puntero al **nombre** (RAM) |
| `+0x08` | u32 | Puntero a la **descripción** (RAM) |
| `+0x10` | u16 | Precio de compra |
| `+0x12` | u16 | Precio de venta |

Los punteros apuntan a cadenas Shift-JIS terminadas en `00` dentro del SLUS o de MAIN (huevos y
familiares, base `0x8000B800`). Varias entradas pueden compartir la misma cadena.
En la categoría Trampas, el "nombre" es la descripción de la trampa.

### 6.3 Reubicación

Cada cadena de objeto tiene un **hueco**: desde su inicio hasta el siguiente byte no nulo del original
(incluye el relleno de alineación). Al escribir una traducción (`ItemsModel.SetString`):

1. Si otra cadena de objeto ya tiene exactamente ese texto, se apunta a ella (p.ej. los tres «Fuego»).
2. Si cabe en su hueco sin pisar otra cadena en uso, se escribe en su sitio.
3. Si no, se coloca en la parte libre de otro hueco (colas de descripciones que han quedado más cortas,
   o huecos abandonados) y se reescribe el puntero del registro.

Nunca se reutiliza un hueco al que apunte algún puntero ajeno a la tabla de objetos (se comprueban
todas las palabras de 32 bits de SLUS y MAIN). Las posiciones de las cadenas de objetos las gestiona
solo esta tabla: la tabla de texto no las escribe, aunque aparezcan en `texto_es.csv`. La pestaña
Objetos muestra los bytes libres que quedan (≈3.000 con la traducción española).

---

## 7. Mensajes de combate y de la torre

Los mensajes de la torre («Subes de nivel», daño, objetos, trampas, maldiciones…) **no están en
Shift-JIS plano**: están comprimidos, por eso ningún detector de texto los ve.

- **Tabla de letras:** `DUNGEON.BIN` `0xFAC8C`, 54 códigos SJIS de 2 bytes (las letras más
  frecuentes del inglés: ` eatnsoirdhcl.upw…`).
- **Bloque de mensajes:** `0xFAD02`–`0xFC9B6`, 280 mensajes seguidos.
- **Formato de un mensaje:**
  - `51 n1 n2 … 00` = tramo comprimido: cada byte `n` es la letra `n` de la tabla (1–54).
  - Fuera de un tramo: `0A` salto de línea, pares SJIS literales (letras que no están en la tabla:
    mayúsculas G J O Q V Z, números, letras con tilde…).
  - `00` fuera de tramo = fin del mensaje.
  - Algunos empiezan con bytes de prefijo (p. ej. `30 30`) que hay que conservar.
- **Punteros:** el código apunta a cada mensaje con `lui`/`addiu` (RAM = offset + `0x7FFE5760`),
  así que **cada mensaje se queda en su offset y con su tamaño exacto**. El sobrante se rellena con
  tramos vacíos `51 00` (no dibujan nada).
- El juego **inserta nombres entre mensajes seguidos** (objeto, monstruo, cantidad): «`Kewne`» +
  « recibe » + «`12`» + « de daño.». Hay que respetar los espacios del principio y del final.
- `∅` en la traducción = mensaje vacío a propósito.

Implementado en `BattleMsgs.cs` y la pestaña **Mensajes combate**.

---

## 8. Textos extra (no detectables)

Algunos textos están rodeados de datos y el detector no los encuentra. Se traducen en
`textos_extra.csv` indicando archivo, offset y tamaño máximo, y se escriben en ese sitio exacto
(con el mismo relleno). En la traducción española hay dos, ambos en `TOWN/TOWN.BIN`:
`0x6340A2` (151 bytes) y `0x6A530B` (172 bytes).

Para encontrar otros: busca el texto en el **Editor hex** («Buscar siguiente»), anota el offset y el
tamaño hasta el siguiente byte que no sea texto, y añade una fila al CSV.

---

## 9. La fuente y la tabla de caracteres

### 9.1 Cómo dibuja el juego una letra

`func_8004D880` (SLUS) convierte el código Shift-JIS en un byte y la celda de la fuente es
`byte − 0x20`:

| Códigos SJIS | Resultado |
|---|---|
| `824F–8258` (０–９) | `'0'–'9'` → celdas 16–25 |
| `8260–8279` (Ａ–Ｚ) | `'A'–'Z'` → celdas 33–58 |
| `8281–829A` (ａ–ｚ) | `'a'–'z'` → celdas 65–90 |
| Resto | Tabla de 40 entradas: claves u16 en RAM `0x8007142C` (SLUS `0x44C2C`), valores u8 en `0x8007147C` (SLUS `0x44C7C`) |

Un código que no está en la tabla se dibuja como **corazón** (por eso no basta con usar códigos
SJIS de letras acentuadas: no existen en la tabla).

### 9.2 El atlas

- Comprimido con el LZ de §10 en **`MAIN.BIN` `0x2257D8`** y una copia idéntica en
  **`DUNGEON.BIN` `0x4C27D8`** (hay que cambiar las dos).
- Descomprimido: 8192 bytes = **128×128 píxeles a 4 bpp** (fila de 64 bytes, nibble bajo = píxel
  par), **16×8 celdas de 8×16**. Se sube a VRAM en (960, 256).
- Valores de píxel 0–15 (0 = transparente; las letras usan 3–8 con suavizado).
- La versión recomprimida debe caber en el hueco del flujo original.

### 9.3 Cómo se añadieron las letras del español

Se reutilizan **10 símbolos de la tabla que ningún texto usa** (solo salen en el teclado de poner
nombre) y se apuntan a celdas que el juego nunca muestra, donde se dibujan las letras nuevas:

| Código | Símbolo original | Letra | Celda |
|---|---|---|---|
| `814F` | ＾ | á | 113 |
| `8151` | ＿ | é | 112 |
| `8160` | ～ | í | 118 |
| `8162` | ｜ | ó | 119 |
| `8165` | ｀ | ú | 115 |
| `8194` | ＃ | ü | 109 |
| `816F` | ｛ | ñ | 111 |
| `8170` | ｝ | Ñ | 123 |
| `818F` | ￥ | ¿ | 126 |
| `8190` | ＄ | ¡ | 127 |

Esto se guarda en **`charmap.txt`** (`814F=á@113`). Al crear el BIN, la herramienta:
1. Dibuja las letras (si no has editado la fuente a mano): copia el acento de la `é` (celda 112) sobre
   las vocales, dibuja `ñ`/`Ñ` y gira 180° `?` y `!` para `¿`/`¡` (`FontPatch.Build`).
2. Escribe en la tabla del SLUS la celda de cada código del charmap (`FontPatch.ApplyTable`).

Efecto secundario: en el teclado de nombres esos 10 símbolos aparecen como las letras nuevas.
Las mayúsculas acentuadas se escriben sin tilde (no hay más símbolos libres seguros).

### 9.4 Añadir otra letra (p. ej. para otro idioma)

1. Pestaña **Fuente** → en la tabla, elige un símbolo que ningún texto use.
2. Escribe la letra en «Letra» y una celda libre (0–127) en «Celda nueva».
3. Pulsa esa celda en el atlas y dibújala (clic izquierdo pinta, derecho borra).
4. **Guardar charmap.txt** y **Guardar fuente en el juego**.
5. Ya puedes escribir esa letra en las traducciones.

También puedes **Exportar PNG** (128×128, gris = valor × 17), editarlo en cualquier programa y
**Importar PNG**.

---

## 10. Gráficos e interfaz (formato comprimido)

### 10.1 Directorio de recursos de imagen

`MAIN.BIN` `0x221800` y `DUNGEON.BIN` `0x4BE800` (mismo contenido): registros de 16 bytes
`tipo u16, 0x0010 u16, tamaño u32, x u16, y u16, w u16, h u16` (x/y/w/h en coordenadas de VRAM,
x y w en *halfwords*), seguidos de los datos. El primer bloque son 54 paletas de 16 colores
(32 bytes cada una). La fuente es la entrada (960, 256, 32, 128).

### 10.2 Compresión (LZ propio, `GfxCodec.cs`)

Flujo de bits de banderas (byte de 8 banderas, se leen del bit bajo al alto):

- `0` → **literal**: copia el siguiente byte.
- `1 0` → **copia larga**: palabra big-endian `w` de 2 bytes. Si `w == 0`, **fin**.
  Distancia = `w >> 4` (1–4095). Longitud = `(w & 0xF) + 2` (3–17); si `w & 0xF == 0`, la longitud
  es el siguiente byte + 1 (1–256).
- `1 1 a b` → **copia corta**: longitud = `(a·2 + b) + 2` (2–5), distancia = siguiente byte (0 = 256).

La herramienta descomprime, deja editar y **recomprime** con el mismo formato (el resultado debe
caber en el hueco original; si no, simplifica el dibujo).

### 10.3 Imágenes con texto del juego

Revisadas todas las texturas comprimidas de MAIN, TOWN y DUNGEON (~480). Solo estas tienen texto:

| Imagen | Ubicación (archivo y offset del flujo) | Formato | Texto original → español |
|---|---|---|---|
| Menú del título | `MAIN.BIN 0x257024` (cabecera en 0x257000, VRAM 704,384) | 4 bpp 128×128 | New Game / Continue / Options → Nueva partida / Continuar / Opciones |
| Pestañas del menú de la torre | `MAIN.BIN 0x224B24` y `DUNGEON.BIN 0x4C1B24` | 4 bpp 128×128 | Items, Select, Line up, Fuse, Command, At Hand, At Feet → Objetos, Elegir, Formar, Fusión, Órdenes, Mano, Suelo |
| Hoja de interfaz | `MAIN.BIN 0x221EDC` y `DUNGEON.BIN 0x4BEEDC` | 4 bpp 128×128 | Yes / No! → Sí / No (HP, MP, Lv se quedan) |
| Iconos de tienda | `MAIN.BIN 0x222B08` y `DUNGEON.BIN 0x4BFB08` | 4 bpp 128×128 | SELL / BUY → VEND / COMP |
| Rótulo de piso | `DUNGEON.BIN 0x4BB2E4` | 8 bpp 128×128 | The Monster Tower → Torre de Monstruos |

Ojo: un flujo real nunca empieza con ceros. El decodificador puede «atravesar» relleno de ceros hasta
llegar a la imagen de verdad (así se tomó por error `0x2554DB` en la v0.4b). Ese relleno es memoria del
juego y escribir en él colgaba la partida nueva al poner nombre. La herramienta rechaza ahora esos offsets.

Además, «NOW LOADING...» es texto ASCII en el ejecutable (`SLUS 0x405B8`, 16 bytes; la `u` hace de
espacio en esa hoja de letras): se traduce como «CARGANDO...» en `textos_extra.csv` con
`codificacion=ascii`.

Las imágenes traducidas van en la carpeta **`imagenes/`** de la traducción, una PNG por textura:
`ARCHIVO@OFFSET.png` (`/` cambiado por `_`, p.ej. `MAIN_MAIN.BIN@224B24.png`), 128 px de ancho, y
el gris de cada píxel es el **índice de color** (4 bpp: gris = índice × 17; 8 bpp: gris = índice). Al
crear el BIN se recomprimen y se escriben en su sitio (`UiImages.cs`). Las de esta traducción se
generan con `Tool/uigen.cs`, que dibuja los textos en español imitando el estilo de cada imagen.

### 10.4 Editar un gráfico de menú/UI

1. Pestaña **Imágenes / UI**: arriba hay un **catálogo** con todos los flujos comprimidos válidos
   encontrados en SLUS, MAIN, TOWN y DUNGEON (miniaturas orientativas a 4 bpp).
2. Pulsa uno: se carga en el editor con «Datos comprimidos» marcado.
3. Ajusta **Formato** (bits/píxel, ancho, alto, tamaño de tile) hasta que la imagen se vea bien.
   La mayoría de texturas de menú son 4 bpp de 128 o 256 de ancho.
4. Para ver los colores reales elige «Paleta → Desde offset del archivo» y pon el offset de una de
   las paletas del directorio (§10.1).
5. Edita con lápiz/relleno o **Exportar PNG** → editar fuera → **Importar PNG** (se ajusta a la
   paleta más cercana).
6. **Guardar en el juego** (recomprime). Después, Crear parche → Crear BIN.

Los gráficos sin comprimir se editan igual con «Datos comprimidos» desmarcado; se guardan al pintar.

---

## 11. Parches PPF

Formato **PPF 3.0** (PPF-O-Matic, MultiPatch, DuckStation…), `Ppf.cs`:

| Offset | Contenido |
|---|---|
| 0 | `"PPF30"` |
| 5 | Método = 2 |
| 6 | Descripción (50 bytes ASCII, rellena con espacios) |
| 56 | Tipo de imagen: 0 = BIN |
| 57 | Blockcheck = 1 (comprueba que el BIN es el correcto) |
| 58 | Undo = 0 |
| 59 | Relleno |
| 60 | 1024 bytes del BIN original en `0x9320` (blockcheck) |
| 1084… | Bloques: offset u64, longitud u8 (1–255), datos |

Los cambios separados por menos de 9 bytes iguales se juntan en un bloque (ocupa menos que dos
cabeceras). La herramienta verifica cada PPF aplicándolo en memoria al BIN original y comparándolo
byte a byte con el BIN traducido.

---

## 12. La herramienta: uso paso a paso

Aplicación de Windows (C# / WinForms, .NET Framework 4, sin dependencias). `Tool/AzureDreamsTool.exe`.
Compilar: `Tool/build.bat` (usa el compilador de C# que trae Windows).

### 12.1 Pestañas

| Pestaña | Para qué |
|---|---|
| **Inicio** | Abrir el BIN original y ver el estado |
| **Texto** | Todos los diálogos y menús. Medidor de espacio, vista de códigos, avisos de maquetación, buscar/reemplazar, botones de letras y códigos |
| **Objetos** | Nombres, descripciones y precios, con reubicación automática |
| **Mensajes combate** | Los 280 mensajes comprimidos de la torre, con medidor de bytes |
| **Fuente** | Atlas editable, tabla de caracteres y charmap |
| **Imágenes / UI** | Catálogo de gráficos comprimidos, editor de píxeles, PNG |
| **Tablas de datos** | Tablas del juego (stats, etc.) con presets |
| **Editor hex** | Cualquier archivo del disco byte a byte, búsqueda de texto |
| **Archivos** | Explorador del disco, extraer/reemplazar archivos |
| **Crear parche** | Carpeta de traducción → comprobar → BIN → PPF → probar |

### 12.2 Hacer (o rehacer) la traducción

1. **Abre el BIN original** (Archivo → Abrir BIN). Nunca se modifica.
2. **Crear parche → Elegir…** la carpeta de traducción (la carpeta `traduccion/` de este
   repositorio, o la tuya) → **Cargar carpeta en el editor**.
3. Revisa/edita en Texto, Objetos, Mensajes combate y Fuente. Los cambios se aplican al momento al
   proyecto en memoria y se autoguardan al cerrar.
4. **Guardar el editor en la carpeta** para guardar tus cambios en las CSV.
5. **1. Comprobar**: errores (no cabe, carácter no válido, mensaje interno desbordado) y avisos
   (líneas de más de 31, ventanas de más de 3 líneas, códigos distintos).
6. **2. Crear BIN traducido** → genera `.bin` + `.cue`. **Probar en DuckStation**.
7. **3. Crear parche PPF** → crea y verifica el `.ppf` para distribuir.

### 12.3 Traducir desde cero a otro idioma

1. Abre el BIN y en Proyecto → **Exportar texto a CSV** / **Exportar objetos a CSV**, y en
   Mensajes combate → **Exportar CSV**. Obtienes las columnas `original` y `traduccion` vacía.
2. Traduce en Excel/LibreOffice (separador `;`, UTF-8) o directamente en la herramienta.
3. Si tu idioma necesita letras que no existen, añádelas en la pestaña **Fuente** (§9.4).
4. Sigue los pasos 5–7 de arriba.

Consejos: respeta los códigos `{…}` (el medidor avisa si faltan o sobran); usa `⏎` solo donde el
original tiene saltos de línea; en los mensajes de combate respeta los espacios de los extremos.

---

## 13. Formato de los archivos de traducción (CSV)

Separador `;`, UTF-8 (con BOM), campos con `;` `"` o saltos de línea entre comillas. Las columnas
pueden ir en cualquier orden; las que no se usan se ignoran.

| Archivo | Columnas que se usan | Clave |
|---|---|---|
| `texto_es.csv` | `archivo`, `offset` (hex), `traduccion` (+ `id`, `max_bytes` informativas) | archivo + offset |
| `objetos_es.csv` | `cat_id`, `num`, `nombre_es`, `descripcion_es`, `compra_es`, `venta_es` | categoría + número |
| `mensajes_es.csv` | `id` (0–279), `traduccion` | id |
| `textos_extra.csv` | `archivo`, `offset` (hex), `max_bytes`, `traduccion`, `codificacion` (vacío = Shift-JIS, `ascii` = un byte por letra) | archivo + offset |
| `charmap.txt` | `CODIGO=letra@celda` | — |
| `imagenes/*.png` | `ARCHIVO@OFFSET.png`, gris = índice de color (§10.3) | archivo + offset |

En la traducción: salto de línea real = `⏎` del juego; `{HEROE}`, `{PAG}`, `{VENT}` y `{xx}` =
códigos de control. Los CSV publicados en `traduccion/` **no incluyen el texto original** del
juego (solo la ubicación y la traducción); la herramienta lo lee de tu propio BIN.

---

## 14. Línea de comandos

```bat
AzureDreamsTool.exe                                   (interfaz gráfica)
AzureDreamsTool.exe --applyall original.bin texto_es.csv objetos_es.csv salida.bin informe.txt
AzureDreamsTool.exe --makeppf original.bin traducido.bin parche.ppf "Descripcion"
AzureDreamsTool.exe --verifyppf original.bin parche.ppf traducido.bin
AzureDreamsTool.exe --guitest original.bin carpeta_traduccion salida.bin   (mismo proceso que la interfaz)
AzureDreamsTool.exe --exportmsgs original.bin mensajes.csv
AzureDreamsTool.exe --checkcsv original.bin texto_es.csv informe.txt
AzureDreamsTool.exe --checkitems original.bin objetos_es.csv informe.txt
AzureDreamsTool.exe --listfiles original.bin
AzureDreamsTool.exe --extractfile original.bin TOWN/TOWN.BIN TOWN.BIN
AzureDreamsTool.exe --extractallstr original.bin carpeta     (vídeos .STR en sectores crudos)
```
`--applyall` busca `mensajes_es.csv` y `textos_extra.csv` en la misma carpeta que `texto_es.csv`,
y `charmap.txt` junto al ejecutable. `--applyall` y la interfaz producen **exactamente el mismo
BIN** (comprobado con MD5).

---

## 15. Código fuente: qué hace cada archivo

| Archivo | Contenido |
|---|---|
| `Disc.cs` | Lectura del BIN MODE2/2352, ISO 9660, escritura de sectores con EDC/ECC |
| `Store.cs` | Proyecto en memoria (copia editable de cada archivo, diferencias, `.azpatch`), mapas RAM |
| `TextTools.cs` | Codec Shift-JIS ↔ texto, detector de textos, relleno (`PadTo`, `PadMessages`), avisos de maquetación, charmap |
| `TextPage.cs` | Pestaña Texto |
| `Items.cs`, `ItemsPage.cs` | Tabla de objetos, reubicación de cadenas, pestaña Objetos |
| `BattleMsgs.cs`, `BattlePage.cs` | Mensajes comprimidos de combate y su pestaña |
| `FontPatch.cs`, `FontPage.cs` | Atlas de la fuente, letras del español, tabla de caracteres, pestaña Fuente |
| `GfxCodec.cs`, `GfxPage.cs` | Compresión LZ de gráficos, catálogo y editor de imágenes |
| `UiImages.cs` | Aplica las imágenes traducidas de la carpeta `imagenes/` |
| `../uigen.cs` | Generador de las imágenes de la interfaz en español (se compila aparte con `GfxCodec.cs`) |
| `TablesPage.cs` | Editor de tablas de datos |
| `HexPage.cs` | Editor hexadecimal y explorador de archivos |
| `Ppf.cs` | Crear y verificar parches PPF 3.0 |
| `BuildPage.cs` | Asistente «Crear parche» y ajustes (`ajustes.txt`) |
| `Csv.cs` | Lectura/escritura de CSV compatibles con Excel |
| `Program.cs` | Ventana principal, menús, línea de comandos |
| `Theme.cs` | Estilo visual |

---

## 16. Pendiente y créditos

**Pendiente:** nada conocido; se aceptan informes de fallos o erratas.

**Créditos e investigación previa:**
- Estructura de la compresión de gráficos y símbolos: proyecto de decompilación comunitario
  [azure-dreams-decomp](https://github.com/thingstuffs/azure-dreams-decomp).
- Tablas de objetos y monstruos: [azure-dreams-modding](https://github.com/RecursiveFunctions/azure-dreams-modding)
  y el randomizer [adrando](https://github.com/ProGrammar-R/ProGrammar-R.github.io).
- Traducción y herramienta: Fveralv.

Azure Dreams es marca de Konami y sus respectivos propietarios. Este proyecto no está afiliado a
Konami y no distribuye datos del juego.
