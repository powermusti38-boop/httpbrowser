# HTTPBrowser / StreamBrowser

Reiner Windows-Browser, Version 1.0.7. Benötigt Microsoft Edge WebView2 Runtime. Die .NET-Laufzeit ist im Download enthalten.

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
