# Guida Utente

Guida completa all'utilizzo di **mcp-servers-for-revit** — come interagire con Revit tramite AI.

---

## Indice

- [Panoramica](#panoramica)
- [Interfaccia in Revit](#interfaccia-in-revit)
- [Chat Panel Integrato](#chat-panel-integrato)
- [Uso con Claude Desktop / Claude Code](#uso-con-claude-desktop--claude-code)
- [Catalogo Comandi](#catalogo-comandi)
- [Esempi Pratici](#esempi-pratici)
- [Flussi di Lavoro Avanzati](#flussi-di-lavoro-avanzati)
- [Convenzioni e Unita' di Misura](#convenzioni-e-unita-di-misura)
- [Domande Frequenti](#domande-frequenti)

---

## Panoramica

mcp-servers-for-revit permette di controllare Autodesk Revit tramite assistenti AI. Funziona in due modalita':

| Modalita' | Come funziona | Quando usarla |
|-----------|---------------|---------------|
| **Chat Panel** | Pannello chat integrato direttamente in Revit | Operazioni rapide, domande sul modello |
| **Client MCP esterno** | Claude Desktop, Claude Code, Cline, ecc. | Automazioni complesse, scripting, analisi approfondite |

### Architettura

```
Tu (prompt) --> Client AI --> Server MCP --> Plugin Revit --> Revit API
                                                    |
                                            Risultati <--
```

Tutte le coordinate sono in **millimetri (mm)**. Il plugin converte automaticamente in piedi (unita' interna di Revit).

---

## Interfaccia in Revit

All'avvio di Revit, il plugin aggiunge un pannello nel ribbon con 3 pulsanti:

### Revit MCP Switch

- **Funzione**: Avvia o ferma il servizio di comunicazione (porta TCP 8080)
- **Quando usarlo**: Clicca una volta per attivare la connessione AI, clicca di nuovo per disattivarla
- **Stato**: Il pannello chat mostra "MCP Online" (verde) o "MCP Offline" (rosso)

### MCP Panel

- **Funzione**: Mostra o nasconde il pannello chat laterale
- **Posizione**: Si aggancia sul lato destro della finestra Revit

### Settings

- **Funzione**: Apre le impostazioni del plugin
- **Configurazioni**: Modello AI, command set, chiave API

---

## Chat Panel Integrato

Il pannello chat permette di interagire con Claude direttamente dentro Revit.

### Layout

```
+-------------------------------------+
| Claude for Revit    MCP Online | Nuova chat |
+-------------------------------------+
|                                     |
|  [C] Claude                         |
|  Ciao! Sono Claude, il tuo         |
|  assistente per Revit.              |
|                                     |
|  [L] Tu                             |
|  Quanti muri ci sono nel modello?   |
|                                     |
|  [C] Claude                         |
|  Ho analizzato il modello...        |
|                                     |
+-------------------------------------+
| [ Rispondi...                    ]  |
| [Sonnet 4 v] [Planning]        [>] |
+-------------------------------------+
```

### Selettore Modello

In basso a sinistra puoi scegliere il modello Claude:

| Modello | Caratteristiche | Consigliato per |
|---------|----------------|-----------------|
| **Haiku 4.5** | Veloce, economico | Domande semplici, operazioni rapide |
| **Sonnet 4** (default) | Bilanciato | Uso generale, buon rapporto qualita'/velocita' |
| **Opus 4** | Piu' capace | Automazioni complesse, analisi approfondite |

Il modello si puo' cambiare in qualsiasi momento — si applica dal messaggio successivo.

### Modalita' Planning

Il pulsante **Planning** attiva il ragionamento esteso (extended thinking):

- **Disattivato** (grigio): Claude risponde direttamente
- **Attivato** (arancione): Claude ragiona step-by-step prima di rispondere

Quando attivo:
- Claude "pianifica" internamente prima di agire
- Il ragionamento appare in chat con sfondo viola e etichetta "Planning"
- L'indicatore mostra "Claude sta pianificando..." durante l'elaborazione
- Utile per operazioni complesse che richiedono piu' passaggi

### Nuova Chat

Clicca **"Nuova chat"** in alto a destra per azzerare la conversazione e ricominciare.

### Messaggi Speciali

| Colore/Stile | Significato |
|-------------|-------------|
| Sfondo bianco, avatar blu | Messaggi tuoi |
| Sfondo crema, avatar arancione | Risposte di Claude |
| Sfondo viola, avatar viola | Ragionamento Planning |
| Sfondo crema, icona fulmine verde | Esecuzione tool Revit |
| Sfondo verde chiaro | Tool completato con successo |
| Sfondo rosso chiaro | Tool fallito con errore |
| Testo grigio | Progresso round (es. "Round 3/15") |

### Feedback durante l'Esecuzione

Durante l'esecuzione di operazioni complesse, il chat panel mostra messaggi di feedback in tempo reale:

- **Testo intermedio**: Quando Claude spiega cosa sta per fare prima di chiamare un tool
- **Tool completato**: Conferma verde con il nome del tool eseguito
- **Tool errore**: Messaggio rosso con dettagli dell'errore
- **Progresso round**: Indicatore del round corrente (fino a 15 round per messaggio)

---

## Uso con Claude Desktop / Claude Code

### Claude Desktop

1. Apri Revit e attiva il servizio MCP (clicca **"Revit MCP Switch"**)
2. Apri Claude Desktop
3. Verifica l'icona del martello (connessione MCP attiva)
4. Scrivi la tua richiesta, es:
   ```
   Analizza il modello Revit aperto e dimmi quanti elementi ci sono per categoria
   ```

### Claude Code (CLI)

```bash
# Esempio: analisi del modello
claude "Usa il tool analyze_model_statistics sul modello Revit aperto"

# Esempio: creazione elementi
claude "Crea 3 livelli a 0, 3500 e 7000 mm nel modello Revit"
```

---

## Catalogo Comandi

Il plugin espone **62 tool** organizzati per categoria.

### Creazione Elementi

| Tool | Descrizione |
|------|-------------|
| `create_level` | Crea livelli a elevazioni specificate (mm) |
| `create_grid` | Crea un sistema di griglie con spaziatura automatica |
| `create_line_based_element` | Crea muri, travi, tubi e altri elementi lineari |
| `create_point_based_element` | Crea porte, finestre, arredi e altri elementi puntuali |
| `create_surface_based_element` | Crea pavimenti, controsoffitti, tetti |
| `create_floor` | Crea un pavimento da profilo |
| `create_room` | Crea e posiziona stanze |
| `create_structural_framing_system` | Crea un sistema di travi strutturali |
| `create_array` | Crea array (copie distribuite) di elementi |
| `copy_elements` | Copia elementi con offset |

### Modifica Elementi

| Tool | Descrizione |
|------|-------------|
| `modify_element` | Modifica geometria di un elemento (sposta, ruota, scala) |
| `set_element_parameters` | Imposta parametri di istanza o tipo |
| `change_element_type` | Cambia il tipo di un elemento |
| `operate_element` | Operazioni multiple: seleziona, colora, nascondi, isola |
| `color_elements` | Colora elementi in base al valore di un parametro |
| `delete_element` | Elimina elementi per ID |
| `match_element_properties` | Copia proprieta' da un elemento sorgente ad altri |
| `override_graphics` | Sovrascrivi la grafica di elementi nella vista |
| `set_element_workset` | Sposta elementi in un altro workset |
| `set_element_phase` | Imposta la fase di un elemento |
| `batch_rename` | Rinomina in batch viste, sheet, livelli, griglie o stanze |
| `renumber_elements` | Rinumera stanze, parcheggi o pannelli in sequenza |

### Query e Estrazione Dati

| Tool | Descrizione |
|------|-------------|
| `get_project_info` | Info progetto: nome, indirizzo, autore, livelli, fasi |
| `analyze_model_statistics` | Conteggio elementi per categoria, tipi, famiglie |
| `get_current_view_info` | Info sulla vista attiva |
| `get_current_view_elements` | Elementi nella vista corrente |
| `get_selected_elements` | Elementi attualmente selezionati |
| `get_element_parameters` | Tutti i parametri di un elemento |
| `ai_element_filter` | Filtro intelligente: cerca elementi per criteri multipli |
| `get_available_family_types` | Tipi di famiglia disponibili nel progetto |
| `get_materials` | Lista materiali con colore e proprieta' |
| `get_material_properties` | Proprieta' dettagliate di un materiale |
| `get_material_quantities` | Quantita' materiali e computo |
| `get_warnings` | Warning e errori del modello |
| `get_worksets` | Lista workset del progetto |
| `get_phases` | Fasi del progetto |
| `get_shared_parameters` | Parametri condivisi nel progetto |
| `export_room_data` | Esporta dati stanze: area, volume, perimetro |
| `get_schedule_data` | Legge i dati di un abaco esistente |
| `export_schedule` | Esporta un abaco in formato testo/CSV |

### Viste e Tavole

| Tool | Descrizione |
|------|-------------|
| `create_view` | Crea viste: pianta, sezione, 3D |
| `create_sheet` | Crea tavole con cartiglio |
| `place_viewport` | Posiziona una vista su una tavola |
| `duplicate_view` | Duplica una vista (con o senza dettagli) |
| `create_view_filter` | Crea filtri di vista con regole |
| `apply_view_template` | Applica un template di vista |
| `create_schedule` | Crea abachi per categoria |

### Annotazioni e Documentazione

| Tool | Descrizione |
|------|-------------|
| `create_dimensions` | Crea quote nella vista corrente |
| `create_text_note` | Crea note di testo |
| `create_filled_region` | Crea regioni riempite |
| `tag_all_walls` | Tagga tutti i muri nella vista |
| `tag_all_rooms` | Tagga tutte le stanze nella vista |
| `create_revision` | Crea una revisione del progetto |

### Gestione Progetto

| Tool | Descrizione |
|------|-------------|
| `add_shared_parameter` | Aggiunge un parametro condiviso |
| `load_family` | Carica una famiglia nel progetto |
| `manage_links` | Gestisci link Revit (carica, scarica, ricarica, rimuovi) |
| `purge_unused` | Identifica/rimuovi famiglie, tipi e materiali non usati |
| `batch_export` | Esportazione batch (DWG, IFC, PDF, NWC) |
| `clash_detection` | Rileva interferenze tra categorie |
| `cad_link_cleanup` | Pulisci link CAD importati |

### Utilita'

| Tool | Descrizione |
|------|-------------|
| `say_hello` | Mostra un messaggio in Revit (test connessione) |
| `send_code_to_revit` | Esegui codice C# arbitrario in Revit |

---

## Esempi Pratici

### Informazioni sul Progetto

```
Dimmi le informazioni del progetto aperto
```
Claude usa `get_project_info` e restituisce nome, indirizzo, autore, livelli e fasi.

### Analisi del Modello

```
Analizza il modello e dimmi quanti elementi ci sono per categoria
```
Claude usa `analyze_model_statistics` e presenta un riepilogo con conteggi per muri, pavimenti, porte, ecc.

### Creazione Livelli

```
Crea 4 livelli:
- Piano Terra a 0 mm
- Piano Primo a 3500 mm
- Piano Secondo a 7000 mm
- Copertura a 10000 mm
```
Claude usa `create_level` per creare tutti i livelli con le elevazioni specificate.

### Creazione Griglie

```
Crea un sistema di griglie 5x4 con spaziatura 6000mm in X e 5000mm in Y
```
Claude usa `create_grid` per generare le griglie con etichette automatiche (A, B, C... e 1, 2, 3...).

### Creazione Muri

```
Crea un muro rettangolare 10x8 metri, spessore 300mm, altezza 3500mm, al livello 0
```
Claude usa `create_line_based_element` per creare 4 muri che formano il rettangolo.

### Creazione Stanze

```
Crea una stanza "Soggiorno" al centro del rettangolo di muri
```
Claude usa `create_room` posizionando la stanza alle coordinate centrali.

### Tagging Automatico

```
Tagga tutte le stanze e tutti i muri nella vista corrente
```
Claude usa `tag_all_rooms` e `tag_all_walls` in sequenza.

### Esportazione Dati

```
Esporta i dati di tutte le stanze con area e volume
```
Claude usa `export_room_data` e restituisce una tabella con nome, numero, livello, area, volume e perimetro.

### Pulizia Modello

```
Controlla i warning del modello e poi identifica gli elementi non usati
```
Claude usa `get_warnings` e `purge_unused` (con dryRun: true per anteprima).

### Computo Materiali

```
Calcola le quantita' di materiali per tutti i muri del progetto
```
Claude usa `get_material_quantities` e restituisce volume, area e peso per materiale.

### Ricerca Elementi Intelligente

```
Trova tutti i muri con altezza maggiore di 3000mm sul livello "Piano Terra"
```
Claude usa `ai_element_filter` con criteri multipli per cercare gli elementi.

### Creazione Vista e Tavola

```
Crea una vista in pianta del Piano Terra in scala 1:100 e posizionala su una nuova tavola A1
```
Claude usa `create_view`, `create_sheet` e `place_viewport` in sequenza.

### Clash Detection

```
Verifica le interferenze tra le strutture e gli impianti meccanici
```
Claude usa `clash_detection` specificando le categorie da controllare.

---

## Flussi di Lavoro Avanzati

### Automazione Multi-Step

Con la modalita' **Planning** attiva, Claude puo' eseguire sequenze complesse:

```
Crea una struttura completa:
1. 3 livelli a 0, 3500 e 7000 mm
2. Griglie 4x3 con spaziatura 6m
3. Muri perimetrali su tutti i livelli
4. Stanze in ogni piano
5. Tagga tutte le stanze
```

Claude pianifichera' tutti i passaggi e li eseguira' in ordine usando fino a 15 round di tool call per messaggio.

### Esecuzione Codice C# Personalizzato

Per operazioni non coperte dai tool standard:

```
Esegui in Revit un codice C# che conta tutti gli elementi di tipo "Generic Model"
e restituisce i loro ID
```

Claude usa `send_code_to_revit` per iniettare ed eseguire codice C# direttamente nella sessione Revit.

> **Attenzione**: `send_code_to_revit` esegue codice arbitrario. Usalo con cautela e verifica sempre il codice prima dell'esecuzione.

### Analisi e Report

```
Genera un report completo del modello:
- Statistiche elementi
- Lista warning
- Elementi non usati
- Dati stanze
```

Claude chiamera' piu' tool in sequenza e combinera' i risultati in un report strutturato.

---

## Convenzioni e Unita' di Misura

### Coordinate

- Tutte le coordinate sono in **millimetri (mm)**
- Il sistema di coordinate segue quello di Revit (X = est, Y = nord, Z = alto)
- Il plugin converte automaticamente mm <-> piedi (1 ft = 304.8 mm)

### Categorie Elementi

Le categorie Revit si specificano con il prefisso `OST_`:

| Categoria | Codice |
|-----------|--------|
| Muri | `OST_Walls` |
| Pavimenti | `OST_Floors` |
| Porte | `OST_Doors` |
| Finestre | `OST_Windows` |
| Pilastri strutturali | `OST_StructuralColumns` |
| Travi | `OST_StructuralFraming` |
| Stanze | `OST_Rooms` |
| Controsoffitti | `OST_Ceilings` |
| Tetti | `OST_Roofs` |
| Scale | `OST_Stairs` |

### ID Elementi

Gli ID sono numeri interi unici per ogni elemento. Si possono ottenere con:
- `get_selected_elements` — elementi selezionati
- `get_current_view_elements` — elementi nella vista
- `ai_element_filter` — ricerca per criteri

### Operazioni Distruttive

Per operazioni come `delete_element` e `purge_unused`, usa sempre `dryRun: true` prima per un'anteprima:

```
Prima mostrami cosa verrebbe eliminato con purge (dry run), poi conferma
```

---

## Domande Frequenti

### Posso usare il plugin senza Claude Desktop?

Si'. Il pannello chat integrato in Revit funziona autonomamente — basta configurare la chiave API Anthropic. Non serve Claude Desktop.

### Posso usare Claude Desktop senza il chat panel?

Si'. Il chat panel e il server MCP sono indipendenti. Claude Desktop si connette tramite il server MCP (Node.js), mentre il chat panel usa direttamente l'API Anthropic.

### Le operazioni sono reversibili?

Revit ha il suo sistema di undo (`Ctrl+Z`). Le operazioni eseguite dai tool vengono registrate nella cronologia undo di Revit, quindi puoi annullarle normalmente.

### Quanti tool call puo' fare Claude per messaggio?

Il chat panel permette fino a **15 round** di tool call per messaggio. Ogni round puo' contenere piu' chiamate parallele. Questo consente automazioni multi-step complesse in un singolo messaggio.

### Funziona con modelli di grandi dimensioni?

Si', ma i tempi di risposta dipendono dalla complessita'. Per modelli molto grandi (>100.000 elementi), le query come `analyze_model_statistics` potrebbero richiedere qualche secondo in piu'.

### Posso usare il plugin in worksharing?

Si'. Il plugin opera nel contesto dell'utente corrente e rispetta i workset. Usa `get_worksets` e `set_element_workset` per gestire i workset.

### Il plugin modifica il modello automaticamente?

No. Claude esegue operazioni **solo quando glielo chiedi**. Il plugin non fa nulla in background senza una tua richiesta esplicita.

### Come scelgo tra Haiku, Sonnet e Opus?

- **Haiku 4.5**: Per domande veloci ("quanti muri ci sono?", "info progetto")
- **Sonnet 4**: Per la maggior parte delle operazioni (creazione elementi, analisi, tagging)
- **Opus 4**: Per automazioni complesse multi-step, analisi approfondite, planning

### Quando attivare il Planning mode?

Attivalo quando la richiesta richiede ragionamento articolato:
- Creazione di strutture complesse (edificio intero)
- Analisi che richiedono piu' passaggi
- Decisioni su quali tool usare e in che ordine
- Debug di problemi nel modello

Per operazioni semplici (query, singola creazione) lascialo disattivato per risposte piu' veloci.
