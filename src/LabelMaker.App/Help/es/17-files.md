---
id: files
title: Proyectos, guardado y configuración
category: Reference
order: 2
keywords: save, open, file, labelproj, autosave, recovery, backup, settings, theme, dark, portable, usb, recent, language, arabic, kurdish, sorani, persian, farsi, french, spanish, dutch, russian, chinese, hindi, update, new version, upgrade, themes, frost, aurora, midnight, graphite, ember, ocean, sakura, sage, glass, custom theme, own colours, make your own theme, guardar, abrir, archivo, guardado automático, recuperación, copia de seguridad, configuración, tema, oscuro, idioma, español, actualización, nueva versión, tema propio
---
# Proyectos, guardado y configuración

## Proyectos

Un proyecto (archivo .labelproj) contiene la hoja de etiquetas, todas las páginas, las imágenes que usa y su conexión de datos. Se pueden tener varios proyectos abiertos a la vez, cada uno en su pestaña sobre la etiqueta. `Archivo › Abrir recientes` muestra los últimos.

## No perder nunca el trabajo

- LabelAxis guarda una **copia de recuperación** cada par de minutos. Si el PC o el programa se detienen de repente, ofrece restaurar el trabajo la próxima vez que se inicie.
- Al guardar se conserva la versión anterior como copia de seguridad junto al archivo (name.labelproj.bak). Puede desactivarlo en `Archivo › Configuración…`, en Guardado automático y recuperación.

## Configuración

`Archivo › Configuración…`: el idioma, el aspecto (ocho temas, claros y oscuros; el botón de tema arriba a la derecha de la ventana principal también los cambia), el tamaño de la cuadrícula y la distancia de ajuste, el guardado automático, la ventana de bienvenida, volver a abrir el último proyecto, las actualizaciones y las confirmaciones antes de quitar páginas o datos.

## Idioma

`Archivo › Configuración… › Idioma` muestra LabelAxis en inglés, árabe, kurdo central (sorani), persa, francés, español, neerlandés, ruso, chino simplificado o hindi. El árabe, el kurdo y el persa disponen las ventanas de derecha a izquierda; los nombres técnicos como PDF, QR Code y Wi-Fi se quedan en inglés. El nuevo idioma se usa después de reiniciar LabelAxis, que se ofrece a hacerlo. La primera vez que se inicia, LabelAxis usa el idioma de Windows si es uno de estos.

## Actualizaciones

LabelAxis busca una versión más reciente aproximadamente una vez al día, y siempre que elija `Ayuda › Buscar actualizaciones…`. Cuando hay una, una barra sobre la etiqueta ofrece `Actualizar ahora`, `Novedades`, `Más tarde` y `Omitir esta versión`. **Actualizar ahora** descarga la nueva versión, comprueba que es exactamente el archivo publicado, cierra LabelAxis (preguntando si quiere guardar su trabajo), la instala y vuelve a iniciar LabelAxis. Se conservan su configuración, plantillas y calibración.

En `Archivo › Configuración… › Actualizaciones` puede desactivar la comprobación diaria o escribir la **dirección de actualización**: la dirección web (https://, por ejemplo una carpeta de publicación de GitHub) o la carpeta compartida donde se publican las actualizaciones de LabelAxis. Una versión compilada con una dirección la usa cuando la casilla está vacía.

## Copia portátil

La versión portátil funciona desde cualquier carpeta o memoria USB. Mientras el archivo **portable.txt** esté junto a LabelAxis.exe, la configuración, sus plantillas y la calibración se guardan en la carpeta **Data** a su lado en lugar de en Windows.

Una copia portátil no instala actualizaciones por sí sola: cuando salga una nueva versión, descomprímala sobre la carpeta (la carpeta Data se conserva).

## Su propio tema

`Archivo › Configuración… › Crear su propio tema…` (o el botón de tema arriba a la derecha) crea un tema con sus colores: elija un tema de partida, un color principal, dos colores de resplandor, un fondo si quiere, y la intensidad de los resplandores. LabelAxis cambia a medida que elige, así que ve el resultado enseguida; los colores del texto se eligen por usted para que siempre se lean. Su tema aparece con los demás; `Quitar mi tema` lo elimina.
