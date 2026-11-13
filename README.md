# HTTPBrowser / StreamBrowser

Reiner Windows-Browser, Version 1.0.8. Benötigt Microsoft Edge WebView2 Runtime. Die .NET-Laufzeit ist im Download enthalten.

Im Vollbild erscheint unten rechts eine Anzeige: Stream-/Seitentitel in Grün, aktuelle Zeit und Gesamtlänge in Gelb sowie der zusammenhängend vorausgepufferte Vorrat in Violett. Alle Zeiten sind Minuten; bei unbekannter Länge steht Live. Eingebettete HTML-Videos werden ebenfalls berücksichtigt. Der Name stammt aus den verfügbaren Medienmetadaten oder ersatzweise dem Seitentitel.

Videos erhalten preload=auto. Beim Pausieren wird weiteres Vorladen angefordert; bei einem von der Seite als window.hls bereitgestellten hls.js-Player wird das Pufferziel für endliche Videos auf die verbleibende Laufzeit angehoben. Die App ersetzt den Player nicht und startet keine eigene Mediendownloads. Webseite, Player, Browser und Speichergrenzen können das Puffern weiterhin begrenzen. Bis zum Filmende vorzuladen ist daher keine allgemeine Garantie. Die Anzeige zeigt den tatsächlich vorhandenen Vorrat vor der Abspielposition.

## Starten

Die ZIP vollständig entpacken. Im Hauptordner liegt nur **StreamBrowser.exe**. Alle Begleitdateien liegen im Ordner **data**, der daneben bleiben muss.

Beim Start erscheinen große farbige Buttons für taraftarium.co/canli/, oha.to und vavoo.to. **+** ergänzt eine Adresse, **−** löscht einen Eintrag; die Auswahl wird lokal gespeichert. Über **★** lässt sich die Auswahl erneut öffnen.

**F11** schaltet rahmenloses Vollbild um, **Esc** beendet es. Der Vollbildknopf eines HTML-Videoplayers aktiviert ebenfalls das App-Vollbild.

HTTP ist in allen Fenstern direkt erlaubt, auch für Medien innerhalb einer HTTPS-Seite. Adressen ohne Protokoll beginnen mit http://. HTTP-Inhalte bleiben unverschlüsselt; Zertifikatsprüfung, SmartScreen und CORS bleiben aktiv.

Angeklickte Quellen öffnen ein weiteres Browserfenster. Automatische Fenster bleiben blockiert und können bei Bedarf über die unten eingeblendete Schaltfläche geöffnet werden.

## Download und Build

[Windows build auf GitHub](https://github.com/powermusti38-boop/httpbrowser/actions/workflows/windows-build.yml) öffnen und aus dem erfolgreichen Lauf unter **Artifacts** die ZIP **httpbrowser-windows-x64** herunterladen.

GitHub Actions prüft die Browsereinstellungen, baut die Windows-Anwendung und den nativen Starter und prüft die Ordnerstruktur sowie den tatsächlichen Start. Der Download enthält nur Anwendung, notwendige Laufzeit-/WebView2-Dateien, diese Anleitung und Lizenzhinweise. Keine Medienwerkzeuge, Videoübernahme oder zusätzlichen Player werden benötigt.

Einstellungen und das Profil HttpBrowserProfile liegen unter %LOCALAPPDATA%\StreamBrowser und sind nicht Bestandteil der ZIP.
