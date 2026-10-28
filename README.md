# HealthyMindIA — App móvil (.NET MAUI)

App Android en .NET MAUI puro para el backend
[backend-movil-habitos-saludables](https://github.com/mguerron22/backend-movil-habitos-saludables).
Se construye en **3 partes**, una por integrante; este README dice qué hace cada una y dónde.

## Estructura

```
src/
  HealthyMind.Mobile.Core   Lógica sin MAUI (se prueba con xUnit, sin emulador):
     Api/                   Cliente HTTP, errores del backend, token automático
     Auth/                  Sesión: login una vez, huella, refresh, modo sin conexión
     Connectivity/          ¿Hay servidor? (red del sistema + ping a /health)
     Data/                  SQLite local (LocalDatabase + LocalSchema)
     Models/                Contratos de la API
     Navigation/, Settings/ Abstracciones que implementa MAUI
     ViewModels/            Un ViewModel por pantalla (CommunityToolkit.Mvvm)
  HealthyMind.Mobile        App MAUI: pantallas XAML, estilos, plataforma
     Views/                 Pantallas
     Controls/              AppHeader, OfflineBanner, FormField (reutilizables)
     Services/              Preferencias, SecureStorage, navegación Shell, diálogos
     Platforms/Android      Huella con BiometricPrompt, permisos, HTTP de desarrollo
     Resources/Styles       Colors.xaml (paleta del mock) y Styles.xaml
tests/
  HealthyMind.Mobile.Core.Tests
```

Regla: **la lógica va en Core** (ViewModel + servicios) y la pantalla XAML solo enlaza datos.
Así se prueba sin emulador y la arquitectura queda igual que en el backend.

## Levantar

1. Backend corriendo (ver su README): `docker compose up -d` y `dotnet run --project src/HealthyMind.Api`.
2. Abrir `HealthyMind.Mobile.slnx` en Visual Studio 2026 (o Rider) y ejecutar en un emulador Android.
   Por consola:
   ```bash
   dotnet build src/HealthyMind.Mobile -t:Run -f net10.0-android
   ```
3. Pruebas:
   ```bash
   dotnet test
   ```

### Emulador

- Usa un emulador con Google APIs (Android 13 o superior). La app llega al backend de tu PC por `http://10.0.2.2:5080`.
- **Huella en el emulador:** en Ajustes → Seguridad → Huella, pon un PIN y registra una huella. Cuando pida tocar el
  sensor, en el emulador abre *Extended controls (…) → Fingerprint → Touch sensor* (o por consola:
  `adb emu finger touch 1`). Sin huella registrada la app funciona igual, solo no ofrece entrar con huella.

### Celular físico

1. Tu PC y el celular en la misma red wifi.
2. Agrega la IP de tu PC en `Platforms/Android/Resources/xml/network_security_config.xml`
   (Android bloquea HTTP salvo en esas direcciones).
3. En el login, toca **Servidor: …** abajo y escribe `http://IP-DE-TU-PC:5080`.
4. Permite el puerto 5080 en el firewall de Windows.

## Cómo funciona el acceso (parte 1)

- **Login una vez:** con usuario o correo y contraseña. El backend devuelve un refresh token que se guarda cifrado
  (`SecureStorage` → Android Keystore). Tras el primer login la app ofrece activar la huella.
- **Después, huella:** al abrir la app se pide la huella; si es correcta se renueva la sesión con el refresh token.
  `SessionManager` renueva **de a una petición a la vez** (el backend corta el dispositivo si se reutiliza un token).
- **Sin internet:** si tras la huella no hay servidor, se entra igual con el perfil guardado en SQLite y se muestra
  el aviso naranja. Al volver la conexión la sesión se renueva sola y el aviso desaparece.
- **Sesión vencida o cuenta suspendida:** la app vuelve sola al login con el motivo.
- Cualquier llamada con `AuthenticatedApiClient` lleva el token y, si recibe 401, renueva y reintenta una vez.

## Las 3 partes

| Parte | Integrante | Alcance |
|---|---|---|
| **1** ✅ | Martín | Base del proyecto, tema y estilos del mock, cliente API, sesión y huella, SQLite, aviso sin internet, Login, Registro, Desbloqueo, menú de pestañas, Perfil básico (huella y cerrar sesión) |
| **2** | — | Inicio y Hábitos |
| **3** | — | Estadísticas, IA y Perfil completo |

### Parte 2 — Inicio y Hábitos (RF02, RF03, RF06, RNF05)

- **Hábitos** (`Views/HabitsPage`, hoy reservada): lista con filtros, crear y editar (nombre, categoría, meta, unidad,
  días, `trackingMode`), pausar y eliminar. Usa `FormField` para el formulario y los endpoints `/api/habits`.
- **Inicio** (`Views/HomePage` + `HomeViewModel`): completar la tarjeta "Energía del día" (x de y hábitos, anillo de
  progreso dibujado con `GraphicsView`, barra) con `GET /api/statistics/dashboard`; lista "Hábitos de hoy" con chip de
  categoría, racha y botón de check que registra el día (`PUT /api/habits/{id}/records/{fecha}`); botón flotante "+".
- **Ubicación opcional** al registrar (`Geolocation` de MAUI → `latitude`/`longitude`).
- **Pasos** (RF06): para hábitos `StepCounter`, leer el podómetro de Android y reportarlo con el mismo PUT.
- **Sin conexión** (lo más importante): tablas en `Data/LocalSchema` para hábitos, registros y una **cola de cambios
  pendientes**; leer siempre de SQLite; al volver la conexión (`IConnectivityService.StatusChanged`) hacer
  `POST /api/sync/push` y luego `GET /api/sync/pull?since=…`. El contrato exacto está en el README del backend
  (Applied / Stale / Rejected, `serverId`, ids GUID generados en el teléfono).

### Parte 3 — Estadísticas, IA y Perfil (RF01, RF04, RF05)

- **Stats** (`Views/StatsPage`): consistencia global, círculos L–D de la semana, barras "Ritmo semanal" y dona
  "Distribución por dimensión", todo con `GraphicsView`. Endpoints `/api/statistics/weekly|monthly|range`.
- **IA** (`Views/InsightsPage`): "Tu Brújula Diaria" con `GET /api/recommendations/latest` e historial. Las
  recomendaciones las genera el backend solo; guardarlas en SQLite para leerlas sin conexión (vienen en el pull).
- **Perfil completo** (`ProfilePage` + `ProfileViewModel`): editar datos, cambiar contraseña, dispositivos con sesión
  (`/api/auth/sessions`) y, si `IsAdmin`, la vista de administración: buscar, suspender y reactivar cuentas.

### Para cualquier parte

- Pantalla nueva: ViewModel en `Core/ViewModels` (registrarlo en `Core/DependencyInjection.cs`), página en `Views`
  (registrarla en `MauiProgram.AddPages`); si se abre encima de otra, ruta en `AppShell.xaml.cs`.
- Colores y estilos: solo los de `Resources/Styles` (`Card`, `Chip`, `PrimaryButton`, `SoftCard`...).
- Errores: `RunAsync` del `ViewModelBase` ya convierte errores del backend y falta de conexión en mensajes.
- Antes de subir: `dotnet test` en verde y la app compilando para Android sin advertencias.
