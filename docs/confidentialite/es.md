# Privacidad — Haloryva Studio 2.7.2

[Français](fr.md) · [English](en.md) · [Español](es.md)

Editor: Julien Lecomte
Contacto público: https://github.com/JulienLecomteGit/haloryva-studio/issues
Versión de la política preparada el 18 de noviembre de 2026 para Haloryva Studio 2.7.2; se aplicará a esa versión cuando esté disponible. La preparación de esta política no significa que la actualización ya esté publicada. La [política histórica 2.7.1](https://github.com/JulienLecomteGit/haloryva-studio/blob/8fa015f798f6cc9803acf40972d2bcac8f28d434/docs/confidentialite/es.md) sigue disponible.


## Tratamiento en tu dispositivo

Haloryva Studio destaca el cursor y permite dibujar anotaciones.
La aplicación trata los eventos del ratón y del teclado para sus efectos, atajos
y acciones de botones, incluso cuando otra ventana está activa. La visualización
de teclas muestra algunas combinaciones con Ctrl, Alt o Windows; no crea un historial
del texto escrito.
La lupa utiliza temporalmente los píxeles alrededor del cursor. Los dibujos y el texto
de las anotaciones se tratan en la sesión de dibujo. Puedes elegir guardarlos mediante
la exportación.
La exportación PNG guarda las anotaciones con fondo transparente en el archivo elegido;
no crea una captura completa del escritorio.


## Ajustes, archivos y copias de recuperación

Un perfil JSON local conserva preferencias como el idioma, las herramientas elegidas,
las acciones de botones y los ajustes visuales. Puede incluir rutas a tus archivos PNG o WAV.
La versión EXE utiliza la carpeta Haloryva de los datos locales de Windows.
La versión Microsoft Store/MSIX utiliza sus propios datos locales de aplicación.
Pueden conservarse copias de seguridad, copias de perfiles no válidos y registros
técnicos locales para recuperación y diagnóstico.

Si aceptas importar los ajustes de la versión clásica, la aplicación crea una copia
independiente. Los PNG/WAV personalizados legibles pueden copiarse con tu consentimiento,
dentro del límite indicado por archivo. También pueden conservarse una copia del
perfil de origen y una copia de seguridad del perfil sustituido. Estas copias contienen
las rutas y preferencias originales; no están anonimizadas.
La importación no elimina los archivos ni la instalación de origen. Puedes rechazarla
o cancelarla. Importar una preferencia anterior de inicio no autoriza por sí solo
el inicio automático en Windows.


## Registros y servicios de red

Un registro de errores puede guardar la fecha, la categoría, el tipo de error y la
traza técnica, con rotación de archivos. Este registro excluye el mensaje y los datos
de la excepción. Los diagnósticos adicionales se activan de forma explícita.
El registro de recuperación de efectos está activo durante el funcionamiento normal. Puede guardar la fecha, la categoría del evento, una razón técnica, un código de Windows y el número de intentos. Es local, se escribe en segundo plano y está limitado a 262 144 bytes (256 KiB); al alcanzar ese límite, las nuevas entradas se detienen sin borrar las existentes. No contiene títulos ni contenido de ventanas, pulsaciones, imágenes ni coordenadas del ratón.

Los modos detallados son opcionales: una sesión de prueba copia los ajustes en una carpeta nueva y puede registrar tiempos, contadores y parámetros de dibujo; el diagnóstico de efectos puede guardar instantáneos del estado de la capa. Estos modos no se activan con un inicio normal y no recogen texto escrito, trayectorias, capturas de pantalla, URL ni títulos de ventanas. La copia del perfil de prueba sí puede contener las mismas rutas de recursos que el perfil original.

Los perfiles y registros no se envían automáticamente al soporte.
Las funciones habituales no requieren una cuenta de Haloryva. Las revisiones del
código de esta versión no han identificado telemetría de la aplicación, publicidad
ni mecanismos de envío a un servidor del editor.
Un archivo en una carpeta sincronizada o de red puede ser tratado por el servicio
que elijas. Windows, Microsoft Store, GitHub y tu proveedor de correo aplican sus
propias políticas.


## Conservación y eliminación

Los archivos locales permanecen hasta su sustitución, rotación o eliminación.
El desinstalador EXE conserva por defecto los ajustes y las copias de seguridad;
eliminarlos es una opción independiente. Esta opción no elimina todas las exportaciones
ni las copias guardadas en otros lugares.
Windows gestiona la desinstalación de la versión Store y puede eliminar los datos
del paquete, incluidas las copias PNG/WAV importadas. Exporta tu perfil y guarda
tus recursos por separado antes de desinstalar. No confíes en una reinstalación para recuperarlos.
Ten también en cuenta las copias de recuperación y los archivos sincronizados al
eliminar tus datos. Haloryva no ofrece cifrado propio de estos archivos ni garantiza
la eliminación de todas sus copias.


## Soporte y solicitudes de privacidad

Enviar información al soporte requiere una acción tuya. El editor utiliza la información
que envías para responder a tu solicitud y comprender el problema comunicado.
Soporte público elegido:
https://github.com/JulienLecomteGit/haloryva-studio/issues
Las incidencias de GitHub son públicas: no adjuntes perfiles personales completos,
registros sin revisar ni capturas con información privada.
Las incidencias pueden seguir visibles tras su cierre y también están sujetas a las políticas de GitHub:
https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement
El canal de contacto elegido es GitHub Issues. Las incidencias son públicas y no constituyen un canal confidencial. No envíes datos sensibles, documentos personales ni solicitudes que contengan información privada. Actualmente no se ofrece ningún canal de soporte privado.
La regla de conservación de tres meses prevista para un eventual soporte privado no se aplica a las incidencias públicas de GitHub. Si se ofrece un canal privado en el futuro, esta política se actualizará antes de su puesta en servicio.
El editor no tiene acceso remoto a los ajustes
que permanecen en tu dispositivo y no puede borrarlos por ti.


## Tus opciones

Puedes cambiar las preferencias, desactivar los efectos, gestionar el inicio en
la aplicación y Windows o salir desde el icono junto al reloj.
«Abrir la carpeta de ajustes» permite localizar tus archivos.
Revisa la información de versión, los perfiles, las anotaciones y los diagnósticos
antes de compartirlos. Esta política se actualizará si cambian las funciones o las
prácticas de tratamiento de datos.

