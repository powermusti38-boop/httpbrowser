# HTTPBrowser / StreamBrowser

Windows-Browser mit direkter HTTP-Unterstützung und Videoübergabe an VLC. Version 1.0.6.

Im entpackten Hauptordner liegt nur **StreamBrowser.exe** als Startdatei. Alle Begleitdateien und Anleitungen liegen im Unterordner **data**. Zum Starten die äußere EXE verwenden; den Ordner data daneben lassen. Der native Starter benötigt selbst keine installierte .NET-Laufzeit.

## Startdialog und Vollbild

Beim Start erscheint eine Auswahl mit großen farbigen Buttons für `https://taraftarium.co/canli/`, `http://oha.to/` und `http://vavoo.to/`. **+ Adresse hinzufügen** ergänzt eine Webadresse; **−** neben einem Eintrag löscht ihn aus der Auswahl. Änderungen werden lokal gespeichert. Über **★** in der Browserleiste lässt sich die Auswahl erneut öffnen. Zusätzliche Quellenfenster öffnen ihre Quelle direkt.

**F11** schaltet rahmenloses Vollbild ein oder aus. **Esc** beendet Vollbild. Auch der Vollbildknopf eines HTML-Videoplayers schaltet das App-Fenster automatisch auf Vollbild; dabei verschwinden Titelleiste, Adressleiste und Statusleiste. Beim Verlassen wird der vorherige Fensterzustand wiederhergestellt.

## Download und Start

Die Windows-Ausgabe wird auf GitHub gebaut: [Windows build](https://github.com/powermusti38-boop/httpbrowser/actions/workflows/windows-build.yml).

1. Den letzten erfolgreichen Lauf öffnen und unter **Artifacts** den Download **httpbrowser-windows-x64** auswählen.
2. Die ZIP vollständig entpacken und **StreamBrowser.exe** starten. Alle Dateien im Ausgabeordner zusammenlassen.
3. Microsoft Edge WebView2 Runtime und VLC müssen installiert sein; die .NET-Laufzeit ist im Download enthalten.

## Direkt als HTTP-Browser

HTTP-Streams sind vom Start an in allen Fenstern erlaubt, auch innerhalb einer HTTPS-Webseite. Eine Adresse ohne Protokoll wird mit `http://` geöffnet. Vollständig angegebene HTTPS-Adressen bleiben HTTPS; Weiterleitungen der jeweiligen Webseite werden weiterhin beachtet.

Der bisherige Knopf **HTTP-Streams …**, der gelbe Umschalt-Hinweis und das separate Kompatibilitätsfenster entfallen. Auch Quellenfenster können direkt zwischen HTTP- und HTTPS-Seiten navigieren.

HTTP-Inhalte sind unverschlüsselt. Die App verwendet dafür den WebView2-Schalter `--allow-running-insecure-content`; auch andere HTTP-Inhalte einer Seite können damit geladen werden. Die Wirkung wird mit der installierten Runtime geprüft, ist aber von deren Unterstützung dieses Schalters abhängig. Zertifikatsprüfung, SmartScreen und CORS bleiben aktiv.

Einstellungen liegen unter `%LOCALAPPDATA%\StreamBrowser`. Das Browserprofil heißt jetzt **HttpBrowserProfile**, damit eine parallel laufende ältere Version nicht ihren Browserprozess ohne HTTP-Unterstützung wiederverwendet. Eine Anmeldung aus einem alten Profil muss daher gegebenenfalls erneut erfolgen.

## Video an VLC übergeben

1. Webseite öffnen und das Video bei Bedarf starten.
2. Maus über das Video bewegen. Die App prüft die Quelle; bei Erfolg erscheint ein grüner Rahmen.
3. **In VLC öffnen** anklicken. Bei mehreren HLS-Kandidaten zuerst die gewünschte Quelle auswählen.
4. Erst wenn VLC tatsächlich Videobilder decodiert, gibt der Browser den betreffenden Stream frei. Normale Videos übernehmen die Abspielzeit, Live-HLS startet am Livepunkt.
5. **Im Browser neu laden** bringt die Webseite zurück. VLC läuft unabhängig weiter.

Falls VLC nicht gefunden wird, über **VLC wählen …** die installierte `vlc.exe` auswählen. DRM, DASH, Sitzungscookies und besondere Zugangskopfzeilen werden nicht an VLC übertragen; eine erreichbare Quelle ist deshalb keine allgemeine Wiedergabegarantie.

## Zusätzliche Seitenfenster

Angeklickte HTTP-/HTTPS-Quellen und Links mit `target="_blank"` öffnen ein vollständiges weiteres StreamBrowser-Fenster mit Videoerkennung und VLC-Schaltfläche. Auch zunächst leere Fenster mit späterer Zieladresse sowie HTTP- und JavaScript-Weiterleitungen werden unterstützt.

Automatisch erzeugte Fenster bleiben blockiert. Bei einer konkreten Zieladresse erscheint unten **Seitenfenster öffnen · …** zur manuellen Öffnung. Maximal sechs zusätzliche Seitenfenster können gleichzeitig offen sein. Downloads, externe Protokollaufrufe und Kamera-/Mikrofon-/Standortberechtigungen bleiben deaktiviert.

## Builds auf GitHub

`.github/workflows/windows-build.yml` läuft bei jedem Push, bei Pull Requests und über **Run workflow**. Der Windows-Runner installiert .NET 10, erzeugt synthetische MP4-/HLS-Testmedien, prüft die Quellen und veröffentlicht eine eigenständig startbare Windows-x64-Ausgabe als Artifact. Die Downloads bleiben 30 Tage verfügbar; ein manueller Lauf erzeugt bei Bedarf einen neuen Build.

Quellcode und Abhängigkeitshinweise werden versioniert. Browserprofile, Einstellungen, Buildausgaben, Diagnoseberichte und die erzeugten Testmedien werden nicht hochgeladen. Die interaktiven WebView2-/VLC-Prüfungen laufen zusätzlich lokal; im GitHub-Workflow laufen die unabhängigen MP4-/HLS-Quellenprüfungen.

## Lokale Prüfungen

Voraussetzung: Windows, .NET SDK 10 und für die Testmedien FFmpeg.

```powershell
./Tests/New-Fixtures.ps1 -IncludeHlsJs
dotnet run --project ./Tests/StreamBrowser.Checks.csproj -- ./Tests/fixtures
```

`--mixed-content-only` prüft tatsächliche HTTP-HLS-Videodecodierung direkt im Hauptfenster und in eingebetteten Playern sowie in einem Quellenfenster. `--popup-only` prüft HTTP als Standardprotokoll, Quellenfenster und die tatsächliche VLC-Übergabe. Diagnoseaufruf für eine lokal gebaute Anwendung:

```powershell
./bin/Release/net10.0-windows/StreamBrowser.exe --diagnostics --fixture-dir C:\StreamBrowser\Tests\fixtures --mixed-content-only
```

Der Diagnosemodus verwendet ein eigenes Browserprofil und stumme eigene VLC-Instanzen. Details zu früheren Abnahmen stehen in `TESTERGEBNISSE.md`.
