// Schnittstelle zwischen der Oberfläche (app.js) und der Desktop-Hülle (Electron).
// preload.js stellt sie per contextBridge als window.pdfwerkHost bereit; im Browser fehlt sie.

/** Eine Datei, die die Hülle an die Oberfläche übergibt. */
interface HostFile {
  /** Dateiname mit Endung, z. B. "Vertrag.pdf" */
  name: string;
  /** Absoluter Pfad auf der Festplatte; die Hülle hat ihn für saveFile freigegeben. Eine entsperrte Kopie (geschützte PDF) übernimmt ihn nicht. */
  path: string | null;
  data: Uint8Array;
}

/** Zustand des Dokuments im Fenster, meldet die Oberfläche bei jeder Änderung. */
interface HostDocState {
  name: string;
  /** Speicherort; die Hülle übernimmt nur Pfade, die sie kennt (geöffnet, gespeichert oder per adoptDroppedFile abgelegt). null = neues Dokument, Speichern fragt nach dem Ort */
  path: string | null;
  /** ungespeicherte Änderungen: Hülle fragt beim Schließen nach, Fenstertitel und macOS-Punkt im Schließknopf */
  dirty: boolean;
  /** nur das unberührte Beispieldokument: die Hülle darf hier eine geöffnete Datei anzeigen statt ein neues Fenster zu öffnen */
  pristine: boolean;
}

/** Menübefehle, die die Hülle an die Oberfläche schickt. */
type HostCommand =
  | 'open' | 'save' | 'saveAs' | 'print' | 'undo' | 'redo' | 'find'
  | 'zoomIn' | 'zoomOut' | 'zoomWidth' | 'zoomPage' | 'zoomActual' | 'toggleSidebar'
  | 'mode:read' | 'mode:comment' | 'mode:pages' | 'mode:tools'
  | 'properties' | 'shortcuts'
  /** Schließen-Rückfrage mit „Speichern“ beantwortet: speichern und bei Erfolg close() aufrufen */
  | 'saveAndClose';

interface PdfwerkHost {
  platform: 'win32' | 'darwin' | 'linux';
  version: string;

  /** Einmal beim Start. Liefert die Dateien für dieses Fenster (Kommandozeile, Doppelklick, macOS open-file); leer = Beispieldokument zeigen. */
  ready(): Promise<HostFile[]>;
  /** Spätere Dateien für dieses Fenster. Die Hülle schickt nur an Fenster, deren Dokument pristine ist; sonst öffnet sie ein neues Fenster. */
  onOpenFiles(cb: (files: HostFile[]) => void): void;
  /** Öffnet den nativen Öffnen-Dialog (PDF und Bilder, Mehrfachauswahl). Ergebnis kommt über onOpenFiles bzw. in neuen Fenstern. */
  openDialog(): Promise<void>;

  /** Überschreibt eine freigegebene Datei (zuvor geöffnet oder im Dialog gewählt) sicher über eine temporäre Datei. Eine per adoptDroppedFile abgelegte Datei
   *  gibt erst eine Rückfrage der Hülle beim ersten Speichern frei. Ist die Datei nicht mehr da (umbenannt, verschoben), wird sie nicht neu angelegt. Wirft Error mit deutscher Meldung. */
  saveFile(path: string, data: Uint8Array): Promise<{ path: string; name: string }>;
  /** Nativer Speichern-unter-Dialog im Ordner des Dokuments (ohne Pfad: der zuletzt geöffneten Datei); schreibt data an den gewählten Ort. null = abgebrochen.
   *  Dateifilter folgt der Endung von defaultName. Eine in einem anderen Fenster geöffnete Datei wird abgelehnt. */
  saveDialog(opts: { defaultName: string; data: Uint8Array; title?: string }): Promise<{ path: string; name: string } | null>;
  /** Pfad einer per Ziehen abgelegten Datei (webUtils.getPathForFile); die Hülle merkt ihn vor (siehe saveFile). null, wenn die Datei keinen Pfad hat. */
  adoptDroppedFile(file: File): string | null;
  /** Ist die Datei in einem anderen Fenster offen, holt die Hülle dieses nach vorn und liefert true. */
  focusOpenFile(path: string): Promise<boolean>;

  /** Fenstertitel, macOS documentEdited und representedFilename, Rückfrage beim Schließen, Wahl des Zielfensters. */
  setDocument(state: HostDocState): void;
  /** Drucken mit dem Druckdialog des Systems: printBegin liefert eine Kennung, dann jede Seite als JPEG per printPage,
   *  printEnd zeigt den Dialog und räumt danach auf; printAbort verwirft einen abgebrochenen Auftrag. Jede Seite füllt das gewählte Papier. */
  printBegin(): Promise<string>;
  printPage(job: string, jpeg: Uint8Array): Promise<void>;
  printEnd(job: string): Promise<void>;
  printAbort(job: string): void;
  /** Menübefehle */
  onCommand(cb: (cmd: HostCommand) => void): void;
  /** Schließt dieses Fenster ohne weitere Rückfrage (nach saveAndClose, wenn alles gespeichert ist). */
  close(): void;
}

interface Window {
  pdfwerkHost?: PdfwerkHost;
  /** Pfad zum pdf.js-Worker, setzt die Desktop-Seite; sonst CDN bzw. eingebetteter Worker */
  PDFWERK_WORKER_SRC?: string;
}
