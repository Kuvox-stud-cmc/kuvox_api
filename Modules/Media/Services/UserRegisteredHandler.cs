using Kuvox.Api.Modules.Auth.Contracts;
using Kuvox.Api.Modules.Media.Enums;
using Kuvox.Api.Modules.Media.Models;
using Kuvox.Api.Modules.Media.Repositories;
using MediatR;

namespace Kuvox.Api.Modules.Media.Services;

internal sealed class UserRegisteredHandler(IAlbumRepository albumRepo) : INotificationHandler<UserRegisteredEvent>
{
  public async Task Handle(UserRegisteredEvent notification, CancellationToken cancellationToken)
  {
    var defaultAlbums = new[]
    {
      new Album
      {
        Name = "Music",
        Description = "Default Audio Album - Music",
        Kind = AlbumKind.Audio,
        MaterialSymbol = "music_note",
        IsDeleteAble = false
      },
      new Album
      {
        Name = "Sound Effects",
        Description = "Default Audio Album - Sound Effects",
        Kind = AlbumKind.Audio,
        MaterialSymbol = "music_cast",
        IsDeleteAble = false
      },
      new Album
      {
        Name = "Voiceover",
        Description = "Default Audio Album - Voiceover",
        Kind = AlbumKind.Audio,
        MaterialSymbol = "record_voice_over",
        IsDeleteAble = false
      }
    };

    foreach (var album in defaultAlbums)
    {
      albumRepo.Add(album);
      albumRepo.AddAlbumUser(new AlbumUser { AlbumId = album.Id, UserId = notification.UserId, Role = Permission.Owner, IsFavorite = false });
    }

    await albumRepo.SaveChangesAsync();
  }
}
