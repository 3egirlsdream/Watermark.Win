﻿namespace Watermark.Andorid
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            MainPage = new MainPage();
        }
        public static object HttpClientHandler;

    }
}