# 101 touches of coding Linux tool with C# for fun

It is hard to start such a blog, but at least it can serve me as feedback during developing a Linux application with C#.

You can ask if I am crazy, C# and Linux it is not a good idea but you know what, I do not care, I know, or at least I think I know how to code within C#. So why not use my existing experience and create something that will help. At least for me. I like UI applications, and Linux has plenty of space where such applications exist only in the form of terminals. For me, I see a space in managing installed software, especially this is a problem in Arch Linux distributions.

So I will try to create a UI tool for installing, uninstalling, updating applications installed on Arch-based distributions of Linux.

You can still ask, why 101 and what touch means. 
Actually, 101 is a cool number, like 101 dalmatians. So it means I do not know how many specific articles for this topic I will write. Problems will show if it is relevant to share it as an article in my blog.

But still, touches? What do you mean by that you can ask? Basically, it will be one touch for one article I would like to share with others. It can refer to some specific problem or solution I will face.

What tools do I plan to use during such a journey?

As the title says I will use C# and Linux, but more specifically I have an idea of what I want to use so in the list below it is written, but by the time I will update it when I use something else, so I can then check backwards as a retrospective my chosen paths. Also next to that, I will use the **[yay](https://github.com/Jguer/yay?msclkid=6fef1093b06e11ecb19936d9641299cf)** tool, which has plenty of possibilities for what I need to execute behind the UI for the user.

- Arch Linux - I will use [Endeavour](https://endeavouros.com/?msclkid=7f2d9b6fb06e11eca3f3c351155ebac9) distribution, currently, the latest is the Atlantis Neo release
- C#
- for UI with C#, I will use the [Avalonia](http://avaloniaui.net/?msclkid=a2aa3738b06d11ecb6db23074ca2617f) framework, together with its Avalonia.Diagnostics and Avalonia.ReativeUI
- [ReactiveUI.Fody](https://www.reactiveui.net/api/reactiveui.fody.helpers/), for simplifying and removing the need of implementing INotifyPropertyChanged interface for each View Model
- for logs, I will use [Serilog](https://serilog.net/?msclkid=e0cc3c02b06d11ecb72a81d232d17cf0) together with Serilog.Sinks.Constole and Serilog.Sinks.File
- [Splat](https://github.com/reactiveui/splat?msclkid=26b1af04b06c11ec80f1a4e22f57b41d) for Service Location together with Spat.Serilog to simplify logging
- [CliWrap](https://github.com/Tyrrrz/CliWrap?msclkid=fb6981c6b06d11ec9a80e1a9fc9df7e8) for executing yay commands
- [NUnit](https://nunit.org/?msclkid=08ca1b59b06e11ec9120f9b6e193ecac) for unit tests
- Currently not sure but maybe also [FluentAssertions](https://fluentassertions.com/?msclkid=190de2f4b06e11ecb8874a3b57d81a23)
- [Specflow](https://specflow.org/?msclkid=247a18f2b06e11eca2d16ddd2bbe5aff) with NUnit for BDD tests
- Own tasks and targets for [MSBuild](https://docs.microsoft.com/en-us/visualstudio/msbuild/msbuild?msclkid=33c4c75db06e11ec89c74519fe334595&view=vs-2022) as a building mechanism
- for CI/CD maybe Azure Dev Ops
- GitHub and maybe GitHub free actions

I decided that my project will be called ****yay_see_sharp.**** Because in base it will be UI for yay in C#.

If you liked this, you can follow my next progress and give me feedback during articles.

Also from this article, you will be able to navigate to my next “touches”.

The list is here: