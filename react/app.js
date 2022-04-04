function renderFullPage(html, preloadedState) {
    return `
    <!doctype html>
    <html>
        <head>
            <title>Redux Universal Example</title>
        </head>
        <body>
            <div id="root">${html}</div>
            <script>
                window.__PRELOADED_STATE__ = ${JSON.stringify(preloadedState)}
            </script>
            <script src="/static/bundle.js"></script>
        </body>
    </html>
    `
}