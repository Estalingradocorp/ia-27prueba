# Fury³ — Busca y encuentra

> **Esta página está siendo diseñada con nuestro modelo `IA 27 Atenea Omega IB`.**

Motor de búsqueda minimalista para el proyecto Fury³. Interfaz de una sola
pantalla: un campo de consulta y un botón. Sin dependencias, sin build,
sin frameworks.

## Demo

<https://estalingradocorp.github.io/ia-27prueba/>

## Características

- HTML, CSS y JavaScript en un único archivo (`index.html`).
- Cero dependencias de runtime.
- Layout responsive mediante `.container` centrado.
- Despliegue automático con GitHub Pages.

## Ver en local

No requiere servidor. Abrí `index.html` en el navegador, o servilo por HTTP:

```bash
python -m http.server 8000
# http://localhost:8000
```

## Estructura

```
.
├── index.html   # página completa (markup + estilos + script)
└── README.md
```

## Despliegue

GitHub Pages publica la rama `main` desde la raíz del repositorio.

| | |
|---|---|
| Repositorio | <https://github.com/Estalingradocorp/ia-27prueba> |
| URL pública | <https://estalingradocorp.github.io/ia-27prueba/> |
| Configuración | Settings → Pages → Source: *Deploy from a branch* → `main` / `(root)` |

Los cambios en `main` se publican en el siguiente build.

## Próximos pasos

- [ ] Conectar la función `doSearch()` a un índice real de contenido.
- [ ] Reemplazar el `alert()` por un renderizado de resultados.
- [ ] Agregar estado de carga y manejo de errores.
- [ ] Modo oscuro y soporte de `prefers-color-scheme`.

---

<p align="center">
  Diseñado y desarrollado con nuestro modelo
  <strong>IA 27 Atenea Omega IB</strong> — Estalíngrado Corp
</p>
