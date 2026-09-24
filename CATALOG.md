# Catálogo visible 2.0.1

Fuente: Subsistence_Custom_Settings_Catalogo_y_UI_CORREGIDO.docx. Los multiplicadores parten de ×1 = Vanilla. Los radios son valores directos.

| Categoría | Ajuste | Rango |
|---|---|---|
| Gathering | Wood yield | ×1–100 |
| Gathering | Wild gatherables | ×1–100 |
| Gathering | Loot crate quantity | ×1–5 |
| Farming | Crop harvest quantity | ×1–100 |
| Animals | Butchering yield | ×1–5 |
| Animals | Passive unless provoked | ON/OFF |
| Crafting | Crafting speed | ×1–100 |
| Traps | Capture attempt speed | ×1–100 |
| Traps | Animal attraction radius | 6000–20000 unidades |
| Traps | Fish trap competition radius | 1–1000 unidades |
| Weapons Bench | Weapon upgrade speed | ×0.1–100 |
| Campfire | Fuel duration | ×0.1–100 |
| BCU | Passive power production | ×0.1–100 |
| BCU | Passive mass production | ×0.1–100 |
| Power Generator | Power output | ×0.1–100 |
| Power Generator | Fuel duration | ×0.1–100 |
| Mass Fabricator | Mass production | ×0.1–100 |
| Mass Fabricator | Power consumption | ×0.1–100 |
| Refinery | Refining speed | ×1–50 |
| Refinery | Power consumption per laser | ×0.1–100 |

Crafting y los intervalos enteros tienen mínimo de un segundo. El resultado muestra la saturación. El loot mantiene selección de objetos, entradas duplicadas y probabilidades originales; los límites de stack del juego siguen aplicándose. Carnicería conserva los valores por especie, incluido jabalí corregido y el Tier6 del alce. El modo pasivo restaura las percepciones individuales al desactivarlo.

Cada perfil genera todos los valores, incluidos Vanilla, en orden determinista. Los 58 comandos de crafting respetan clases base antes de excepciones; el perfil completo tiene 154 comandos. No hay dos ajustes propietarios del mismo destino.

La investigación y los baselines exactos están en src/SCS.Core/Model/catalog-baselines.json, con párrafo de procedencia; las reglas están en SupportedCatalog.cs. No se habilitan candidatos ni funciones excluidas por el documento. El soporte refleja la evidencia aportada por el usuario, sin afirmar pruebas nuevas dentro del juego.
