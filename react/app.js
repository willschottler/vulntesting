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

express().get('/news', (req, res) => {
    let topic = req.query.topic;
    res.send(`<h1>${topic}</h1>`);
});

class Clock extends React.Component {
    constructor(props) {
        super(props);
        this.state = { date: new Date() };
    }

    render() {
         // GOOD: this.state.date is defined above
        var now = this.state.date.toLocaleTimeString()
        return (
                <div>
                <h2>The time is {now}.</h2>
                </div>
        );
    }
}