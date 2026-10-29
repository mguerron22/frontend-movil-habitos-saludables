# HealthyMindIA — App móvil (.NET MAUI)

App Android en .NET MAUI puro para el backend
[backend-movil-habitos-saludables](https://github.com/mguerron22/backend-movil-habitos-saludables).
Se construye en **dos mitades**, una por integrante; este README dice qué hace cada una y dónde.

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

## Reparto (2 integrantes, 50 % cada uno)

| Mitad | Integrante | Alcance |
|---|---|---|
| **1** ✅ | Martín | Base del proyecto, estilos del mock, cliente API, sesión y huella, aviso sin internet, Login, Registro, Desbloqueo, Perfil básico **+ Hábitos, Inicio completo y sincronización sin conexión** |
| **2** ✅ | e1724741283-arch | Estadísticas, IA, Perfil completo y administración, pasos y ubicación |

### Lo que ya está (mitad 1): hábitos y sin conexión

- **Primero local:** las pantallas leen y escriben siempre en SQLite (`Data/HabitStore`). Cada cambio entra a una
  cola (`Data/SyncOutbox`) y `Sync/SyncService` la envía (`/api/sync/push`) y luego descarga lo nuevo
  (`/api/sync/pull`). Sin internet todo funciona igual y se envía solo al volver la conexión.
- Para crear, editar, pausar, borrar o registrar avance se usa **siempre `IHabitService`**: guarda local, encola y pide
  sincronizar. No llamar a los endpoints de hábitos directamente.
- `HabitInsights` calcula sobre lo local el progreso de hoy y las rachas.
- Controles para reutilizar: `ProgressRing` (anillo) y `RoundedProgressBar` (barra), dibujados con `GraphicsView`;
  `CategoryInfo` trae nombre, ícono y colores de cada categoría.

### Lo que ya está (mitad 2): estadísticas, IA, perfil y sensores (RF01, RF03, RF04, RF05, RF06)

- **Stats** (mock 3): `Habits/StatisticsCalculator` calcula sobre SQLite, con la misma regla de "día esperado" que el
  backend, así que funciona sin conexión. Semana/Mes, consistencia con tendencia contra el periodo anterior, círculos
  L–D, barras "Ritmo semanal" (`WeeklyBarsChart`), dona por dimensión (`DonutChart`) y detalle por hábito.
- **IA** (mock 4, "Tu Brújula Diaria"): las recomendaciones llegan dentro de `/api/sync/pull` y se guardan en la tabla
  `recommendations` (`Data/RecommendationStore`): se leen también sin conexión. El mejor y el peor día de la semana se
  calculan en el teléfono con las últimas 4 semanas. Deslizar hacia abajo sincroniza.
- **Perfil completo:** editar nombre, apellido y correo (`PUT /api/profile`; el perfil guardado se actualiza para el
  modo sin conexión), cambiar contraseña (cierra las demás sesiones y conserva la de este teléfono), "Mis
  dispositivos" (cerrar una sesión o todas las demás) y, solo para Admin, **Administrar usuarios**: buscar, filtrar
  por estado, suspender y reactivar (el admin no puede suspenderse a sí mismo). Estas pantallas necesitan conexión.
- **Pasos (RF06):** `Habits/StepTracker` toma el contador acumulado de Android (`TYPE_STEP_COUNTER`, permiso
  `ACTIVITY_RECOGNITION`) y lo convierte en pasos del día (`StepDay`: cambio de día y reinicio del teléfono). Llena
  solo los hábitos "Contar pasos automáticamente" al abrir o refrescar Inicio, y nunca baja un valor ya registrado.
  Sin sensor (el emulador no lo trae, ni Windows) esos hábitos se siguen anotando a mano.
- **Ubicación (RF03, opcional):** interruptor "Guardar ubicación" en Perfil (apagado por defecto; pide el permiso al
  activarlo). Si está activo, cada registro manual guarda latitud y longitud; sin GPS se guarda igual, sin ubicación.

### Para cualquier parte

- Pantalla nueva: ViewModel en `Core/ViewModels` (registrarlo en `Core/DependencyInjection.cs`), página en `Views`
  (registrarla en `MauiProgram.AddPages`); si se abre encima de otra, ruta en `AppShell.xaml.cs`.
- Colores y estilos: solo los de `Resources/Styles` (`Card`, `Chip`, `PrimaryButton`, `SoftCard`...).
- Errores: `RunAsync` del `ViewModelBase` ya convierte errores del backend y falta de conexión en mensajes.
- Antes de subir: `dotnet test` en verde y la app compilando para Android sin advertencias.
