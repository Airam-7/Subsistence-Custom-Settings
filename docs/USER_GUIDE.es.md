# Subsistence Custom Settings 2.0.5

Para quitar hotkeys, retirar perfiles y recuperar respaldos, consulta la [guía de desinstalación y recuperación](RECOVERY.md). Las instrucciones de compilación, CLI y pruebas visuales están en [BUILD.md](../BUILD.md).

Esta compilación se entrega en Windows-2.0.5, sin reemplazar ejecutables anteriores. Cierra la versión anterior de la app y abre este ejecutable; verifica v2.0.5 en el lateral.

## Guardado y cierre

- Save profile guarda únicamente el perfil activo en Binaries y su estado local. No guarda borradores de otros perfiles ni las nuevas asignaciones de hotkeys.
- Save all guarda todos los perfiles pendientes (incluidas migraciones), las asignaciones de hotkeys como preferencias locales y la ruta de instalación. No escribe UDKInput.ini ni instala hotkeys.
- Install hotkeys sigue siendo la operación explícita que valida disponibilidad y modifica el INI, permitida con el juego abierto.

Save all está disponible desde cualquier pantalla. Se deshabilita cuando no quedan cambios pendientes y muestra Unsaved changes de forma discreta cuando los hay. Una hotkey guardada pero aún no instalada no es un cambio sin guardar: la pantalla diferencia Saved hotkey de Installed hotkey.

Al cerrar con pendientes se ofrecen Save all and close / Discard changes / Cancel. Guardar cierra únicamente si el guardado completo termina correctamente. Descartar abandona los borradores aún no guardados; no revierte archivos que ya se hayan guardado. Cancelar conserva la ventana y los borradores.

Antes de escribir perfiles, Save all valida todos los perfiles pendientes y las asignaciones locales. Las teclas sin asignar se pueden guardar como preferencia; claves no admitidas o asignaciones duplicadas se deben corregir. Una tecla ocupada por el juego puede permanecer guardada como preferencia, pero no instalarse. Guardar solo preferencias no requiere una instalación válida; escribir perfiles sí exige Binaries verificado y escribible.

Tras escribir un perfil o las preferencias se vuelven a leer los bytes para confirmar el resultado. Solo entonces se reconoce el guardado. Un error de validación no inicia escrituras. Si ocurre un error de disco después de guardar algunos perfiles, esos guardados se conservan y se informa que la operación quedó incompleta; los restantes siguen pendientes. No se cierra automáticamente y se puede reintentar.

El aviso anterior contemplaba otros perfiles, migraciones y asignaciones pendientes, aunque el perfil activo estuviera guardado. Ahora esos pendientes tienen un estado global visible y una acción de guardado global. Cambiar de pantalla o tener hotkeys pendientes de instalación no provoca por sí solo un aviso de cierre.

## Funcionalidad conservada

20 ajustes, 154 comandos, catálogo 2.0.1. Perfiles custom con identidad fija, Vanilla de solo lectura y actualización con backup. Refinery Speed hasta ×50 y consumos positivos en UI.

El INI usa ColdGame.ColdPlayerInput si contiene bindings ajenos a SCS; en caso contrario usa Engine.PlayerInput. Se conserva la reparación al inicio/Verify y la escritura de hotkeys con el juego abierto. Backups, preservación de contenido ajeno y comprobación de cambios concurrentes siguen activos. Save all no invoca esa reparación ni la instalación de hotkeys.

## Validación

El artefacto de release pasó 97 casos de regresión en el entorno de publicación, incluidos dos fixtures INI locales opcionales. El árbol público limpio ejecuta 95 pruebas portables sin necesitar esos fixtures privados. La compilación finalizó sin errores ni advertencias. El ejecutable publicado pasó además los escenarios WPF de Save profile aislado, Save all de varios perfiles y preferencias, INI intacto, botón deshabilitado después de guardar, reapertura sin pendientes, validación previa sin escrituras, fallo parcial de perfil, fallo de metadata, reintento y las tres opciones reales del diálogo.

Las pruebas escribieron en instalaciones sintéticas; no se modificó la instalación real del juego. No se realizó una nueva sesión de gameplay.
