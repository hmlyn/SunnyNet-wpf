package main

var app = NewApp()

func shutdownApp() {
	if app == nil || app.App == nil {
		return
	}

	app.App.SetIeProxy(true)
	app.App.Close()
}
